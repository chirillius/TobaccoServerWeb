using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    [DisallowConcurrentExecution]
    public class ConversionRegisterJob : IJob
    {
        public const string DefaultConversionZoneName = "Конверсия";
        private static int _totalPeopleCounter = 0;
        private static DateTime _currentRegisterDate = DateTime.MinValue;
        private static readonly object _sessionLock = new();
        private static readonly ConversionSessionTracker _sessionTracker = new(TimeSpan.FromSeconds(600));
        private static readonly List<Mat> _sessionImages = new();
        private const int MaxSessionImages = 9;

        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope?.ServiceProvider.GetService<AppDbContext>();
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;

            if (db is null || videoService is null)
            {
                return;
            }

            var now = DateTime.Now;
            var today = now.Date;
            await EnsureCounterLoadedAsync(db, today);

            if (_currentRegisterDate != today)
            {
                await TryFinalizeActiveSessionAsync(db, now, force: true);
                await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);
                await LoadCounterForDateAsync(db, today);
            }

            var zonesConfigurator = new ZonesConfigurator();
            var zones = zonesConfigurator.GetZones();
            var conversionZoneNamePart = context.MergedJobDataMap["conversionZoneNamePart"]?.ToString();
            var legacyClientZoneNamePart = context.MergedJobDataMap["legacyClientZoneNamePart"]?.ToString();
            var stallZoneNamePart = context.MergedJobDataMap["stallZoneNamePart"]?.ToString()?.ToLower();

            if (string.IsNullOrWhiteSpace(stallZoneNamePart))
            {
                await TryFinalizeActiveSessionAsync(db, now);
                return;
            }

            var conversionRegisterZone = ResolveConversionZone(zones, legacyClientZoneNamePart, conversionZoneNamePart);
            var nextFireTime = context.Trigger.GetNextFireTimeUtc();
            var thresholdDateTime = now.AddHours(1);

            if (nextFireTime.HasValue && nextFireTime.Value.ToLocalTime().DateTime > thresholdDateTime)
            {
                await TryFinalizeActiveSessionAsync(db, now, force: true);
                await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);
            }

            if (conversionRegisterZone?.ConversionCounting?.EntryBand is null)
            {
                await TryFinalizeActiveSessionAsync(db, now);
                return;
            }

            var stallZones = zones.Where(x => x.Name.ToLower().Contains(stallZoneNamePart)).ToList();
            if (stallZones.Count == 0)
            {
                await TryFinalizeActiveSessionAsync(db, now);
                return;
            }

            var stallCounts = await Task.WhenAll(stallZones.Select(x => videoService.GetPeopleNumberAsync(x)));
            if (stallCounts.Sum() <= 0)
            {
                await TryFinalizeActiveSessionAsync(db, now);
                return;
            }

            var directionalCount = await videoService.GetDirectionalEntryCountAsync(conversionRegisterZone);
            if (directionalCount.NewEntries > 0 || HasActiveSession())
            {
                var shouldCapturePhoto = ApplySessionObservation(now, directionalCount);
                if (shouldCapturePhoto)
                {
                    await AddSessionImageAsync(videoService, conversionRegisterZone, directionalCount.ImageBase64);
                }
            }

            await TryFinalizeActiveSessionAsync(db, now);
        }

        private static async Task EnsureCounterLoadedAsync(AppDbContext db, DateTime targetDate)
        {
            if (_currentRegisterDate == targetDate)
            {
                return;
            }

            if (_currentRegisterDate == DateTime.MinValue)
            {
                await LoadCounterForDateAsync(db, targetDate);
            }
        }

        private static async Task LoadCounterForDateAsync(AppDbContext db, DateTime targetDate)
        {
            var nextDate = targetDate.AddDays(1);
            var existingRecord = await db.ConversionRegister
                .Where(x => x.DateTime >= targetDate && x.DateTime < nextDate)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            _currentRegisterDate = targetDate;
            _totalPeopleCounter = existingRecord?.PeopleNumber ?? 0;
        }

        private static async Task UpsertDailyRegisterAsync(AppDbContext db, DateTime targetDate, int peopleNumber)
        {
            if (targetDate == DateTime.MinValue)
            {
                return;
            }

            var nextDate = targetDate.AddDays(1);
            var existingRecord = await db.ConversionRegister
                .Where(x => x.DateTime >= targetDate && x.DateTime < nextDate)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (existingRecord is null)
            {
                db.ConversionRegister.Add(new ConversionRegister
                {
                    DateTime = targetDate,
                    PeopleNumber = peopleNumber,
                    DefectImage = CreateDefectImage()
                });
            }
            else
            {
                existingRecord.PeopleNumber = peopleNumber;
                existingRecord.DateTime = targetDate;
            }

            await db.SaveChangesAsync();
        }

        private static bool HasActiveSession()
        {
            lock (_sessionLock)
            {
                return _sessionTracker.HasActiveSession;
            }
        }

        private static bool ApplySessionObservation(DateTime now, TobaccoEntities.Models.Neuro.DirectionalEntryCountResponse directionalCount)
        {
            lock (_sessionLock)
            {
                return _sessionTracker.ApplyObservation(now, directionalCount);
            }
        }

        private static async Task AddSessionImageAsync(
            VideoCacheService videoService,
            Zone conversionZone,
            string? entryImageBase64 = null)
        {
            var fullFrame = DecodeEntryImage(entryImageBase64)
                ?? await videoService.TakeShotAsync(conversionZone.CameraAddress);

            if (fullFrame.Empty())
            {
                fullFrame.Dispose();
                return;
            }

            lock (_sessionLock)
            {
                _sessionImages.Add(fullFrame);
                while (_sessionImages.Count > MaxSessionImages)
                {
                    var oldestImage = _sessionImages[0];
                    _sessionImages.RemoveAt(0);
                    oldestImage.Dispose();
                }
            }
        }

        private static Mat? DecodeEntryImage(string? entryImageBase64)
        {
            if (string.IsNullOrWhiteSpace(entryImageBase64))
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(entryImageBase64);
                var image = Cv2.ImDecode(bytes, ImreadModes.Color);
                if (!image.Empty())
                {
                    return image;
                }

                image.Dispose();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task TryFinalizeActiveSessionAsync(AppDbContext db, DateTime now, bool force = false)
        {
            ConversionSessionSnapshot? snapshot = null;
            List<Mat> sessionImages;

            lock (_sessionLock)
            {
                if (!_sessionTracker.HasActiveSession || (!force && !_sessionTracker.ShouldFinalize(now)))
                {
                    return;
                }

                snapshot = _sessionTracker.Finalize();
                sessionImages = _sessionImages.ToList();
                _sessionImages.Clear();
            }

            string? imagePath = null;
            try
            {
                if (sessionImages.Count > 0)
                {
                    using var grid = DefectImagesSaver.CreateImageGrid(sessionImages);
                    if (!grid.Empty())
                    {
                        imagePath = DefectImagesSaver.Save("Grid", grid, "conversion");
                    }
                }
            }
            finally
            {
                sessionImages.ForEach(x => x.Dispose());
            }

            _totalPeopleCounter += snapshot.PeopleNumber;
            await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);

            db.ConversionRegisterEvents.Add(new ConversionRegisterEvent
            {
                DateTime = snapshot.StartedAt,
                PeopleNumber = snapshot.PeopleNumber,
                DefectImage = CreateDefectImage(imagePath)
            });
            await db.SaveChangesAsync();
        }

        private static DefectImage CreateDefectImage(string? path = null)
        {
            return new DefectImage { Path = path ?? string.Empty };
        }

        public static Zone? ResolveConversionZone(
            IEnumerable<Zone> zones,
            string? legacyClientZoneNamePart,
            string? configuredConversionZoneNamePart)
        {
            var zonesWithEntryBand = zones
                .Where(zone => zone.ConversionCounting?.EntryBand is not null)
                .ToList();

            if (zonesWithEntryBand.Count == 0)
            {
                return null;
            }

            return FindZoneByNamePart(zonesWithEntryBand, configuredConversionZoneNamePart)
                ?? FindZoneByNamePart(zonesWithEntryBand, DefaultConversionZoneName)
                ?? FindZoneByNamePart(zonesWithEntryBand, legacyClientZoneNamePart)
                ?? zonesWithEntryBand.First();
        }

        private static Zone? FindZoneByNamePart(IEnumerable<Zone> zones, string? zoneNamePart)
        {
            if (string.IsNullOrWhiteSpace(zoneNamePart))
            {
                return null;
            }

            return zones.FirstOrDefault(zone =>
                zone.Name.Contains(zoneNamePart, StringComparison.OrdinalIgnoreCase));
        }
    }
}

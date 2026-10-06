using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public enum ClearStallSessionDisposition
    {
        None,
        ResetOnly,
        SaveAndReset
    }

    public class ClearStallDetectionJob : IJob
    {
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static object _lock = new object();
        private static List<Mat> _cachedImages = new List<Mat>();

        private static bool HasValidCachedImages()
        {
            return _cachedImages.Any(image => image is not null && !image.Empty());
        }

        private static TimeSpan GetInactivityThreshold(TimeSpan period)
        {
            return TimeSpan.FromTicks(period.Ticks * 2);
        }

        private static void ResetState()
        {
            _checkTimes.Clear();
            _cachedImages.ForEach(x => x.Dispose());
            _cachedImages.Clear();
        }

        public static ClearStallSessionDisposition GetSessionDisposition(int checkTimesCount, DateTime? lastDetectedAt, DateTime now, TimeSpan period)
        {
            if (checkTimesCount <= 0 || lastDetectedAt is null || period <= TimeSpan.Zero)
            {
                return ClearStallSessionDisposition.None;
            }

            if (now - lastDetectedAt.Value < GetInactivityThreshold(period))
            {
                return ClearStallSessionDisposition.None;
            }

            return checkTimesCount > 5
                ? ClearStallSessionDisposition.SaveAndReset
                : ClearStallSessionDisposition.ResetOnly;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope?.ServiceProvider.GetService<AppDbContext>()
                    ?? throw new InvalidOperationException("AppDbContext scope was not provided for ClearStallDetectionJob.");
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService
                    ?? throw new InvalidOperationException("VideoCacheService was not provided for ClearStallDetectionJob.");
                var period = TimeSpan.FromMilliseconds(int.Parse(context.MergedJobDataMap["period"].ToString()));
                var zonesConfigurator = new ZonesConfigurator();
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"]?.ToString();

                if (string.IsNullOrWhiteSpace(zoneNamePart))
                {
                    throw new InvalidOperationException("zoneNamePart was not provided for ClearStallDetectionJob.");
                }

                var zones = zonesConfigurator.GetZones()
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name) &&
                        x.Name.Contains(zoneNamePart, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!zones.Any())
                {
                    logger?.LogWarning(
                        "ClearStallDetectionJob skipped because no zones matched zoneNamePart '{ZoneNamePart}'. Check Configuration/zones.json and periodal_tasks.json.",
                        zoneNamePart);
                    return;
                }

                lock (_lock)
                {
                    var now = DateTime.Now;
                    DateTime? lastDetectedAt = _checkTimes.Count > 0 ? _checkTimes.Last() : null;
                    var sessionDisposition = GetSessionDisposition(_checkTimes.Count, lastDetectedAt, now, period);

                    if (sessionDisposition == ClearStallSessionDisposition.SaveAndReset)
                    {
                        if (HasValidCachedImages())
                        {
                            using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                            var path = DefectImagesSaver.Save("Grid", grid, "clearStall");

                            _ = db.ClearStallFailures.Add(new ClearStallFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last(), DefectImage = new DefectImage() { Path = path } });
                            _ = db.SaveChanges();
                        }
                        else
                        {
                            logger?.LogWarning(
                                "ClearStallDetectionJob skipped saving defect from {StartDateTime} to {EndDateTime} because no valid cached images were available.",
                                _checkTimes.First(),
                                _checkTimes.Last());
                        }
                    }

                    if (sessionDisposition != ClearStallSessionDisposition.None)
                    {
                        ResetState();
                    }
                }

                var stallStates = await Task.WhenAll(zones.Select(x => videoService.IsStallSurfaceClearAsync(x)));
                var isDetected = stallStates.Any(x => x == false);
                var shouldCaptureImages = false;

                lock (_lock)
                {
                    var now = DateTime.Now;
                    if (isDetected && (_checkTimes.Count == 0 || now - _checkTimes.Last() < GetInactivityThreshold(period)))
                    {
                        _checkTimes.Add(now);
                        shouldCaptureImages = !HasValidCachedImages() && _checkTimes.Count > 1;
                    }
                }

                if (shouldCaptureImages)
                {
                    var shots = await Task.WhenAll(zones.Select(zone => videoService.TakeShotAsync(zone.CameraAddress)));
                    // Capture the actual contour in the evidence, so later zone edits do not erase it.
                    for (var i = 0; i < shots.Length; i++)
                    {
                        if (shots[i] is not null && !shots[i].Empty())
                        {
                            var frameZone = ZoneGeometry.ForFrame(zones[i], shots[i].Width, shots[i].Height);
                            ZoneImageProcessing.DrawOutline(shots[i], frameZone, new Scalar(255, 170, 80));
                        }
                    }
                    var validShots = shots.Where(image => image is not null && !image.Empty()).ToList();
                    foreach (var invalidShot in shots.Where(image => image is null || image.Empty()))
                    {
                        invalidShot?.Dispose();
                    }

                    lock (_lock)
                    {
                        if (!HasValidCachedImages())
                        {
                            _cachedImages.AddRange(validShots);
                        }
                        else
                        {
                            validShots.ForEach(image => image.Dispose());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "ClearStallDetectionJob");
            }
        }
    }
}

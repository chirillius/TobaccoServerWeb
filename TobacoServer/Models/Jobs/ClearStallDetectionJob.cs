using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class ClearStallDetectionJob : IJob
    {
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static object _lock = new object();
        private static List<Mat> _cachedImages = new List<Mat>();

        private static bool HasValidCachedImages()
        {
            return _cachedImages.Any(image => image is not null && !image.Empty());
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
                var interval = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000 + 60;
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
                    if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, interval))
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
                    if (_checkTimes.Count >= 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, interval))
                    {
                        _checkTimes.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                    }
                }

                var stallStates = await Task.WhenAll(zones.Select(x => videoService.IsStallSurfaceClearAsync(x)));
                var isDetected = stallStates.Any(x => x == false);
                var shouldCaptureImages = false;

                lock (_lock)
                {
                    var now = DateTime.Now;
                    if (isDetected && (_checkTimes.Count == 0 || _checkTimes.Count > 0 && now - _checkTimes.Last() < new TimeSpan(0, 0, interval)))
                    {
                        _checkTimes.Add(now);
                        shouldCaptureImages = !HasValidCachedImages() && _checkTimes.Count > 1;
                    }
                }

                if (shouldCaptureImages)
                {
                    var shots = await Task.WhenAll(zones.Select(zone => videoService.TakeShotAsync(zone.CameraAddress)));
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

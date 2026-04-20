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
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "clearStall");

                        _ = db.ClearStallFailures.Add(new ClearStallFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last() });
                        _ = db.SaveChanges();
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

                lock (_lock)
                {
                    if (isDetected && (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, interval)))
                    {
                        if (!_cachedImages.Any() && _checkTimes.Count > 1)
                        {
                            foreach (var zone in zones)
                            {
                                var image = videoService.TakeShotAsync(zone.CameraAddress).Result;
                                _cachedImages.Add(image);
                            }
                        }
                        _checkTimes.Add(DateTime.Now);
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

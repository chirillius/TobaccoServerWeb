using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class LightDetectionJob : IJob
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
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                    var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                    var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
                    var period = 3 * int.Parse(context.MergedJobDataMap["period"].ToString());
                    var zonesConfigurator = new ZonesConfigurator();
                    var zones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();

                    if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, period / 1000))
                    {
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "light");
                        _ = db.LightFailures.Add(new LightFailure()
                        {
                            StartDateTime = _checkTimes.First(),
                            EndDateTime = _checkTimes.Last(),
                            DefectImage = new DefectImage() { Path = path }
                        });
                        _ = db.SaveChanges();
                        _checkTimes.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                    }

                    if (_checkTimes.Count == 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, period))
                    {
                        _checkTimes.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                    }

                var lightIsOn = zones.Select(async x => await videoService.AreLightsOn(x)).Any(x => x.Result == true);

                if (lightIsOn)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, period))
                    {
                        _checkTimes.Add(DateTime.Now);
                        var usedAddresses = new List<string>();
                        if (!_cachedImages.Any())
                        {
                            foreach (var zone in zones)
                            {
                                if (!usedAddresses.Contains(zone.CameraAddress))
                                {
                                    _cachedImages.Add(videoService.TakeShotAsync(zone.CameraAddress).Result);
                                    usedAddresses.Add(zone.CameraAddress);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "LightDetectionJob");
            }
        }
    }
}

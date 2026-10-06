using Microsoft.IdentityModel.Tokens;
using Quartz;
using System.IO;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class BadgeDetectionJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private static int _totalBadgeRequests = 0;
        private static int _positiveBadgeRequests = 0;
        private string _imagePath;
        private static List<string> _imagesCounter = new List<string>();
        private static object _checkTimesLock = new object();
        private static object _lock = new object();

        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();

                lock (_checkTimesLock)
                {
                    if (context.NextFireTimeUtc.Value.LocalDateTime > DateTime.Now.AddHours(4) && _totalBadgeRequests != 0)
                    {
                        var failure = new BadgeFailure()
                        {
                            DateTime = DateTime.Now,
                            DefectImage = new DefectImage()
                        };
                        var ratio = (double)_positiveBadgeRequests / _totalBadgeRequests;
                        if (ratio < 0.5 && _totalBadgeRequests > 0)
                        {
                            _ = db.BadgeFailures.Add(failure);
                            _ = db.SaveChanges();
                        }
                        var defectName = failure.Name;
                        _defectImageService.MoveDefectImagesWithDateAsync(defectName, DateTime.Now.ToString("dd-MM-yyyy:HH-mm-ss-ffff"), _imagesCounter).Wait();
                        _imagesCounter.Clear();
                        _totalBadgeRequests = 0;
                        _positiveBadgeRequests = 0;

                    }

                    var isSalesmanPresent = zones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Any(x => x.Result > 0);
                    if (!isSalesmanPresent)
                    {
                        return;
                    }

                    foreach (var zone in zones)
                    {
                        _imagePath = videoService.IsBadgeOnPerson(zone).Result;
                        if (!_imagePath.IsNullOrEmpty())
                        {
                            _positiveBadgeRequests++;
                            _imagesCounter.Add(_imagePath);
                        }
                        _totalBadgeRequests++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "BottleDetectionJob");
            }
        }
    }
}

using Microsoft.IdentityModel.Tokens;
using Quartz;
using System.IO;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;
using TobacoServer.Services;

namespace TobacoServer.Models.Jobs
{
    public class MoppingJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        static bool _wasMoppingDetected = false;
        static int count = 0;
        const int maxCount = 5;
        private static object _checkTimesLock = new object();
        private string _imagePath;
        private static Dictionary<DateTime, string> _imagesCounter = new Dictionary<DateTime, string>();

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
                var intervalKey = context.MergedJobDataMap["intervalKey"].ToString();
                var intervalEndTime = TimeSpan.Parse(context.MergedJobDataMap["intervalEndTime"].ToString());
                var period = TimeSpan.FromMilliseconds(Convert.ToInt32(context.MergedJobDataMap["period"]));
                var scheduledFireTime = context.FireTimeUtc.LocalDateTime;
                var zonesConfigurator = new ZonesConfigurator();

                var zones = zonesConfigurator.GetZones();
                var moppingZones = zones.Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();

                lock (_checkTimesLock)
                {
                    if (ScheduledWindowGuard.IsFinalFireInConfiguredWindow(scheduledFireTime, intervalEndTime, period))
                    {
                        if (!ScheduledWindowGuard.TryClaimFinalFire($"MoppingJob:{intervalKey}", scheduledFireTime, intervalEndTime, period))
                        {
                            return;
                        }

                        var failure = new MoppingFailure()
                        {
                            DateTime = DateTime.Now,
                            DefectImage = new DefectImage()
                        };
                        var defectName = failure.Name;
                        var falsePositiveDate = DateTime.Now.ToString("dd_MM_yyyy");
                        _defectImageService.MoveImagesToFalsePositiveWithDateAsync(
                            defectName,
                            falsePositiveDate,
                            _imagesCounter.Values.ToList()).Wait();
                        if (!_wasMoppingDetected)
                        {
                            _ = db.MoppingFailures.Add(failure);
                            _ = db.SaveChanges();
                            count = 0;
                            return;
                        }

                        _imagesCounter.Clear();
                        _wasMoppingDetected = false;
                    }

                    if (_wasMoppingDetected)
                    {
                        return;
                    }
                    var isMoppingDetected = false;
                    foreach (var zone in moppingZones)
                    {
                        _imagePath = videoService.IsMoppingDetectedAsync(zone).Result;
                        if (!_imagePath.IsNullOrEmpty())
                        {
                            count++;
                            _imagesCounter[DateTime.Now] = _imagePath;
                            if (count >= maxCount)
                            {
                                _wasMoppingDetected = true;
                                count = 0;
                            }
                            break;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                var logger = context.MergedJobDataMap["logger"] as ILogger;
                logger?.LogError(ex, "Error in MoppingJob");
                throw;
            }

        }
    }
}

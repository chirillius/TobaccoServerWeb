using Microsoft.IdentityModel.Tokens;
using NAudio.SoundFont;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Jobs;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

internal class CashRegisterRecountingJob : IJob
{
    private readonly DefectImageService _defectImageService = new DefectImageService();
    private static object _lock = new object();
    private static bool _isDetected;
    private string _imagePath;
    private static List<string> _imagesCounter = new List<string>();

    public async Task Execute(IJobExecutionContext context)
    {
        var logger = context.MergedJobDataMap["logger"] as ILogger;
        try
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope.ServiceProvider.GetService<AppDbContext>();
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
            var zonesConfigurator = new ZonesConfigurator();
            var zones = zonesConfigurator.GetZones();
            var intervalKey = context.MergedJobDataMap["intervalKey"].ToString();
            var intervalEndTime = TimeSpan.Parse(context.MergedJobDataMap["intervalEndTime"].ToString());
            var period = TimeSpan.FromMilliseconds(Convert.ToInt32(context.MergedJobDataMap["period"]));
            var scheduledFireTime = context.FireTimeUtc.LocalDateTime;
            var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();
            var cashRegisterZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
            CashRegisterRecountingStatus.EnterWindow(intervalKey);
            var isLastFireInCurrentWindow = IsLastFireInCurrentWindow(scheduledFireTime, intervalEndTime, period);

            lock (_lock)
            {
                if (isLastFireInCurrentWindow)
                {
                    if (!ScheduledWindowGuard.TryClaimFinalFire($"CashRegisterRecountingJob:{intervalKey}", scheduledFireTime, intervalEndTime, period))
                    {
                        CashRegisterRecountingStatus.ExitWindow(intervalKey);
                        return;
                    }

                    if (!_isDetected)
                    {
                        var failure = new CountingCashRegisterFailure()
                        {
                            DateTime = DateTime.Now,
                            DefectImage = new DefectImage()
                        };
                        _ = db.CountingCashRegisterFailures.Add(failure);
                        _ = db.SaveChanges();
                    }

                    if (_isDetected && _imagesCounter.Count > 0)
                    {
                        var defectName = $"{DateTime.Now:dd-MM-yyyy:HH-mm-ss-ffff}";
                        _defectImageService.MoveDefectImagesWithDateAsync(defectName, DateTime.Now.ToString("dd-MM-yyyy:HH-mm-ss-ffff"), _imagesCounter).Wait();
                    }

                    _imagesCounter.Clear();
                    _isDetected = false;
                    CashRegisterRecountingStatus.ExitWindow(intervalKey);
                    return;
                }
            }

            var totalClientsNumber = clientZones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();
            lock (_lock)
            {
                if (totalClientsNumber > 0 || _isDetected)
                    return;
            }

            lock (_lock)
            {
                foreach (var zone in cashRegisterZones)
                {
                    var imagePathsResult = new List<string>();
                    for (int i = 0; i < 5; i++)
                    {
                        _imagePath = videoService.IsCashRegisterOpenAsync(zone).Result;
                        imagePathsResult.Add(_imagePath);
                    }
                    var cashRegisterCounter = imagePathsResult.Count(x => !x.IsNullOrEmpty());
                    var threshold = Math.Ceiling(imagePathsResult.Count / 2.0);

                    if (cashRegisterCounter >= threshold)
                    {
                        _isDetected = true;
                        _imagesCounter.AddRange(imagePathsResult.Where(x => !string.IsNullOrEmpty(x)));
                        CashRegisterRecountingStatus.MarkCompleted();
                        return;
                    }
                }
            }



        }
        catch (Exception ex)
        {
            logger.LogJobError(ex, "CountingCashRegisterCheckJob");
        }
    }

    private static bool IsLastFireInCurrentWindow(DateTime now, TimeSpan intervalEndTime, TimeSpan period)
    {
        return ScheduledWindowGuard.IsFinalFireInConfiguredWindow(now, intervalEndTime, period);
    }
}

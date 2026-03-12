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
            var morningTimeToEnd = DateTime.Parse(context.MergedJobDataMap["morningTimeToEnd"].ToString());
            var eveningTimeToEnd = DateTime.Parse(context.MergedJobDataMap["eveningTimeToEnd"].ToString());
            var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();
            var cashRegisterZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
            var period = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000;


            lock (_lock)
            {
                if (context.NextFireTimeUtc.Value.LocalDateTime > DateTime.Now.AddHours(+1))
                {
                    var failure = new CountingCashRegisterFailure()
                    {
                        DateTime = DateTime.Now,
                        DefectImage = new DefectImage()
                    };
                    if (!_isDetected)
                    {
                        CashRegisterRecountingStatus.IsRecountingInProgress = false;
                        _ = db.CountingCashRegisterFailures.Add(failure);
                        _ = db.SaveChanges();
                        return;
                    }
                    var defectName = failure.Name;
                    var defectId = failure.Id;
                    _defectImageService.MoveDefectImagesWithDateAsync(defectName, DateTime.Now.ToString("dd-MM-yyyy:HH-mm-ss-ffff"), _imagesCounter).Wait();
                    _imagesCounter.Clear();
                    _isDetected = false;
                    CashRegisterRecountingStatus.IsRecountingInProgress = false;
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
                        CashRegisterRecountingStatus.IsRecountingInProgress = true;
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
}

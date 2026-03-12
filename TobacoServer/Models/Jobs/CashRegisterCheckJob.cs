using Microsoft.IdentityModel.Tokens;
using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    internal class CashRegisterCheckJob : IJob
    {
        private static object _checkTimesLock = new object();
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static Dictionary<string, DateTime> _imagesCounter = new Dictionary<string, DateTime>();
        private static object _lock = new object();
        private string _imagePath;
        private static List<Mat> _cachedImages = new List<Mat>();
        private static Dictionary<string, string> _cachedImagesPaths = new Dictionary<string, string>();

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
                var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();
                var cashRegisterZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
                var period = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000;

                lock (_checkTimesLock)
                {
                   
                    if (CashRegisterRecountingStatus.IsRecountingInProgress)
                    {
                        _imagesCounter.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                        _cachedImagesPaths.Clear();
                        _checkTimes.Clear();
                        return;
                    }

                    if (_checkTimes.Count > 2 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, period * 3))
                    {
                        var images = _defectImageService.GetImageWithResultAsync(_cachedImagesPaths.Values.ToList()).Result;
                        foreach (var item in images)
                        {
                            
                            _cachedImages.Add(item);
                        }
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "cashRegister");

                        var failure = new CashRegisterFailure()
                        {
                            StartDateTime = _imagesCounter.First().Value,
                            EndDateTime = _imagesCounter.Last().Value,
                            DefectImage = new DefectImage() { Path = path }
                        };
                        _ = db.CashRegisterFailures.Add(failure);
                        _ = db.SaveChanges();
                        var defecName = failure.Name;
                        var defectId = failure.Id;
                        _defectImageService.MoveDefectImagesAsync(defecName, defectId, _imagesCounter.Keys.ToList()).Wait();
                        _imagesCounter.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                        _cachedImagesPaths.Clear();
                        _checkTimes.Clear();
                    }

                    if (_checkTimes.Any() && _checkTimes.Count <= 2 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, period * 3))
                    {
                        _imagesCounter.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                        _cachedImagesPaths.Clear();
                        _checkTimes.Clear();
                    }
                }

                var totalClientsNumber = clientZones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();

                if (totalClientsNumber > 0)
                {
                    return;
                }


                lock (_lock)
                {
                    foreach (var zone in cashRegisterZones)
                    {
                        var imagePathsResult = new List<(string Path, DateTime Time)>();

                        for (int i = 0; i < 5; i++)
                        {
                            var imagePath = videoService.IsCashRegisterOpenAsync(zone).Result;
                            imagePathsResult.Add((imagePath, DateTime.Now));
                        }

                        var cashRegisterCounter = imagePathsResult.Count(x => !string.IsNullOrEmpty(x.Path));

                        var threshold = Math.Ceiling(imagePathsResult.Count / 2.0);

                        if (cashRegisterCounter >= threshold)
                        {
                            _checkTimes.Add(DateTime.Now);

                            foreach (var item in imagePathsResult)
                            {
                                if (!string.IsNullOrEmpty(item.Path))
                                {
                                    _imagesCounter[item.Path] = item.Time;
                                }
                            }

                            var lastValid = imagePathsResult.LastOrDefault(x => !string.IsNullOrEmpty(x.Path));

                            if (!string.IsNullOrEmpty(lastValid.Path))
                            {
                                _cachedImagesPaths[zone.CameraAddress] = lastValid.Path;
                            }
                        }


                        imagePathsResult.Clear();
                    }

                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "CashRegisterCheckJob");
            }
        }
    }
}

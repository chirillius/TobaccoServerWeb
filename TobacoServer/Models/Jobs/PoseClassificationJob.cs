using Microsoft.IdentityModel.Tokens;
using NAudio.SoundFont;
using OpenCvSharp;
using Quartz;
using System.Drawing.Text;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class PoseClassificationJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private string _imagePath;
        private static List<string> _allImagesPaths = new List<string>();
        private static Dictionary<string, string> _cachedImagesPaths = new Dictionary<string, string>();
        private static bool _isRunning = false;
        private static Dictionary<DateTime, string> _imagesCounter = new Dictionary<DateTime, string>();
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
                var timeOffsetInMilliseconds = int.Parse(context.MergedJobDataMap["timeOffsetInMilliseconds"].ToString());
                var lengthOfSamplesListToAverage = int.Parse(context.MergedJobDataMap["lengthOfSamplesListToAverage"].ToString());
                var timeDelta = int.Parse(context.MergedJobDataMap["timeDelta"].ToString());
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                var poseZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
                var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();

                var stallZonePeopleNumber = poseZones.Sum(x => videoService.GetPeopleNumberAsync(x).Result);
                if (stallZonePeopleNumber == 0)
                {
                    return;
                }

                var totalClientsNumber = clientZones.Sum(x => videoService.GetPeopleNumberAsync(x).Result);
                if (totalClientsNumber == 0)
                {
                    if (_isRunning)
                    {
                        lock (_lock)
                        {
                            _isRunning = false;
                            _cachedImages.ForEach(x => x.Dispose());
                            _cachedImages.Clear();
                            _imagesCounter.Clear();
                            _allImagesPaths.Clear();
                            _cachedImagesPaths.Clear();
                        }
                    }
                    return;
                }

                if (_isRunning)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_isRunning)
                    {
                        return;
                    }

                    totalClientsNumber = clientZones.Sum(x => videoService.GetPeopleNumberAsync(x).Result);
                    if (totalClientsNumber == 0)
                    {
                        return;
                    }

                    _isRunning = true;
                }


                Thread.Sleep(timeOffsetInMilliseconds);

                foreach (var zone in poseZones)
                {
                    var imagePathsResult = new Dictionary<DateTime, string>();
                    for (var i = 0; i < lengthOfSamplesListToAverage; i++)
                    {
                        var currentClients = clientZones.Sum(x => videoService.GetPeopleNumberAsync(x).Result);
                        if (currentClients == 0)
                        {
                            return;
                        }

                        if (videoService.GetPeopleNumberAsync(zone).Result > 0)
                        {
                            _imagePath = videoService.IsPoseDetectedAsync(zone).Result;
                            imagePathsResult.Add(DateTime.Now, _imagePath);
                        }
                        Thread.Sleep(timeDelta);
                    }

                    if (imagePathsResult.Count > 0)
                    {
                        var poseCounter = imagePathsResult.Count(x => x.Value.Contains("sitting"));
                        if (poseCounter >= Math.Ceiling(imagePathsResult.Count / 2.0))
                        {
                            foreach (var count in imagePathsResult)
                            {
                                if (count.Value.Contains("sitting"))
                                {
                                    _imagesCounter[count.Key] = count.Value;
                                }
                                _allImagesPaths.Add(count.Value);
                            }
                            _cachedImagesPaths[zone.CameraAddress] = _imagesCounter.Last().Value;

                            var images = await _defectImageService.GetImageWithResultAsync(_cachedImagesPaths.Values.ToList());
                            foreach (var image in images)
                            {
                                _cachedImages.Add(image);
                            }
                            using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                            var path = DefectImagesSaver.Save("Grid", grid, "pose");
                            var failure = new PoseFailure()
                            {
                                DateTime = _imagesCounter.First().Key,
                                DefectImage = new DefectImage() { Path = path }
                            };
                            db.PoseFailures.Add(failure);
                            db.SaveChanges();

                            await _defectImageService.MoveDefectImagesAsync(failure.Name, failure.Id, _allImagesPaths);

                            _imagesCounter.Clear();
                            _cachedImages.ForEach(x => x.Dispose());
                            _cachedImages.Clear();
                            _allImagesPaths.Clear();
                            _cachedImagesPaths.Clear();
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                lock (_lock)
                {
                    _isRunning = false;
                }
                logger.LogJobError(ex, "PoseClassificationJob");
            }
        }

    }
}

using Microsoft.IdentityModel.Tokens;
using OpenCvSharp;
using Quartz;
using System.Data;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class SmokeDetectionJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private string _imagePath;
        private static object _lock = new object();
        private static Dictionary<string, DateTime> _cachedImagesPaths = new Dictionary<string, DateTime>();
        private static List<Mat> _cachedImagesWithBboxes = new List<Mat>();
        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
                var clientZoneNamePart = context.MergedJobDataMap["clientZoneNamePart"].ToString();
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                var smokeZones = zones.Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();
                var clientZones = zones.Where(x => x.Name.ToLower().Contains(clientZoneNamePart.ToLower())).ToList();

                var clientNumber = clientZones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();
                lock (_lock)
                {
                    if (clientNumber > 0)
                    {
                        foreach (var zone in smokeZones)
                        {
                            _imagePath = videoService.FindSmokeAsync(zone).Result;
                            if (!_imagePath.IsNullOrEmpty() && !_imagePath.Contains("no-smoke"))
                            {
                                _cachedImagesPaths[_imagePath] = DateTime.Now;
                            }
                        }

                        if (!_cachedImagesPaths.IsNullOrEmpty())
                        {
                            var imageWithMetadata = _defectImageService.GetImageWithBboxAsync(_cachedImagesPaths.Keys.ToList()).Result;
                            foreach (var item in imageWithMetadata)
                            {
                                var image = item.Image;
                                var imageZone = item.Zone;
                                var bboxes = item.Bboxes;
                            ZoneImageProcessing.DrawOutline(image, imageZone, new Scalar(0, 255, 255));

                                foreach (var rect in bboxes)
                                {
                                    var restoredRectangle = new Rectangle(imageZone.Rectangle.X + rect.X,
                                        imageZone.Rectangle.Y + rect.Y, rect.Width, rect.Height);
                                    Cv2.Rectangle(image, restoredRectangle.ToRect(), new Scalar(255, 0, 0), 4);
                                }
                                _cachedImagesWithBboxes.Add(image);
                            }
                            using var grid = DefectImagesSaver.CreateImageGrid(_cachedImagesWithBboxes);
                            var path = DefectImagesSaver.Save("Grid", grid, "smoke");

                            var failure = new Smoke()
                            {
                                DateTime = _cachedImagesPaths.First().Value,
                                DefectImage = new DefectImage() { Path = path }
                            };
                            _ = db.SmokeFailures.Add(failure);
                            _ = db.SaveChanges();
                            var defectName = failure.Name;
                            var defectId = failure.Id;
                            _defectImageService.MoveDefectImagesAsync(defectName, defectId, _cachedImagesPaths.Keys.ToList()).Wait();
                            _cachedImagesPaths.Clear();
                            _cachedImagesWithBboxes.ForEach(x => x.Dispose());
                            _cachedImagesWithBboxes.Clear();
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "SmokeDetectionJob");
            }
        }
    }
}

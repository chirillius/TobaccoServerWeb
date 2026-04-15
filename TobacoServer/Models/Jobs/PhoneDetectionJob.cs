using Microsoft.IdentityModel.Tokens;
using OpenCvSharp;
using Quartz;
using System.Runtime.CompilerServices;
using TagLib.Flac;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class PhoneDetectionJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private string _imagePath;
        private static List<string> _cachedImagesPaths = new List<string>();
        private static List<Mat> _cachedImagesWithBboxes = new List<Mat>();
        private static Dictionary<string, DateTime> _imagesCounter = new Dictionary<string, DateTime>();
        private static object _lock = new object();
        private static List<string> _usedAddresses = new List<string>();
        private static bool _isPhotoNeeded = true;


        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var interval = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000;
                var zonesConfigurator = new ZonesConfigurator();
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
                var zones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();

                lock (_lock)
                {
                    if (_imagesCounter.Count > 3 && DateTime.Now - _imagesCounter.Last().Value > new TimeSpan(0, 0, 15 * interval))
                    {
                        var imageWithMetadata = _defectImageService.GetImageWithBboxAsync(_cachedImagesPaths).Result;
                        foreach (var item in imageWithMetadata)
                        {
                            var image = item.Image;
                            var imageZone = item.Zone;
                            var bboxes = item.Bboxes;

                            foreach (var rect in bboxes)
                            {
                                var restoredRectangle = new Rectangle(imageZone.Rectangle.X + rect.X,
                                    imageZone.Rectangle.Y + rect.Y, rect.Width, rect.Height);
                                Cv2.Rectangle(image, restoredRectangle.ToRect(), new Scalar(255, 0, 0), 4);
                            }
                            _cachedImagesWithBboxes.Add(image);
                        }

                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImagesWithBboxes);
                        var path = DefectImagesSaver.Save("Grid", grid, "phones");

                        var failure = new PhoneFailure()
                        {
                            StartDateTime = _imagesCounter.First().Value,
                            EndDateTime = _imagesCounter.Last().Value,
                            DefectImage = new DefectImage() { Path = path }
                        };
                        _ = db.PhoneFailures.Add(failure);
                        _ = db.SaveChanges();
                        var defectName = failure.Name;
                        var defectId = failure.Id;
                        _defectImageService.MoveDefectImagesAsync(defectName, defectId, _imagesCounter.Keys.ToList()).Wait();
                        _usedAddresses.Clear();
                        _imagesCounter.Clear();
                        _cachedImagesWithBboxes.ForEach(x => x.Dispose());
                        _cachedImagesWithBboxes.Clear();
                        _cachedImagesPaths.Clear();
                        _isPhotoNeeded = true;

                    }

                    if (_imagesCounter.Count <= 3 && _imagesCounter.Any() && DateTime.Now - _imagesCounter.Last().Value > new TimeSpan(0, 0, 15 * interval))
                    {
                        _usedAddresses.Clear();
                        _imagesCounter.Clear();
                        _cachedImagesWithBboxes.ForEach(x => x.Dispose());
                        _cachedImagesWithBboxes.Clear();
                        _cachedImagesPaths.Clear();
                        _isPhotoNeeded = true;
                    }
                }

                var clientZones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();
                if (clientZones.Select(x => videoService.GetPeopleNumberAsync(x)).All(x => x.Result <= 0))
                {
                    return;
                }



                lock (_lock)
                {
                    foreach (var zone in zones)
                    {
                        _imagePath = videoService.FindPhonesAsync(zone).Result;
                        if (!_imagePath.IsNullOrEmpty())
                        {
                            _imagesCounter.Add(_imagePath, DateTime.Now);

                            if (_isPhotoNeeded && !_usedAddresses.Contains(zone.CameraAddress))
                            {
                                _cachedImagesPaths.Add(_imagePath);
                                _usedAddresses.Add(zone.CameraAddress);
                            }
                        }
                    }

                    if (_isPhotoNeeded && _imagesCounter.Count > 0)
                    {
                        _isPhotoNeeded = false;
                    }

                }

            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "PhoneDetectionJob");
            }
        }
    }
}

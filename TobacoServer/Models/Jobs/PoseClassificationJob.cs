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
    public class PoseClassificationJob : IJob
    {
        private readonly DefectImageService _defectImageService = new DefectImageService();
        private static bool _isRunning = false;
        private static readonly object _lock = new object();

        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;

            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope?.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var timeOffsetInMilliseconds = int.Parse(context.MergedJobDataMap["timeOffsetInMilliseconds"].ToString());
                var lengthOfSamplesListToAverage = int.Parse(context.MergedJobDataMap["lengthOfSamplesListToAverage"].ToString());
                var timeDelta = int.Parse(context.MergedJobDataMap["timeDelta"].ToString());
                var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString().ToLower();
                var clientZoneNamePart = context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower();

                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                var poseZones = zones.Where(x => x.Name.ToLower().Contains(zoneNamePart)).ToList();
                var clientZones = zones.Where(x => x.Name.ToLower().Contains(clientZoneNamePart)).ToList();

                if (db is null || videoService is null || poseZones.Count == 0 || clientZones.Count == 0)
                {
                    return;
                }

                var posePeopleNumber = GetPeopleNumber(videoService, poseZones);
                var clientPeopleNumber = GetPeopleNumber(videoService, clientZones);

                if (clientPeopleNumber == 0)
                {
                    ResetRunningState();
                    return;
                }

                if (posePeopleNumber == 0)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_isRunning)
                    {
                        return;
                    }

                    _isRunning = true;
                }

                Thread.Sleep(timeOffsetInMilliseconds);

                clientPeopleNumber = GetPeopleNumber(videoService, clientZones);
                if (clientPeopleNumber == 0)
                {
                    ResetRunningState();
                    return;
                }

                foreach (var zone in poseZones)
                {
                    if (videoService.GetPeopleNumberAsync(zone).Result <= 0)
                    {
                        continue;
                    }

                    var sampledImages = new Dictionary<DateTime, string>();

                    for (var i = 0; i < lengthOfSamplesListToAverage; i++)
                    {
                        if (GetPeopleNumber(videoService, clientZones) == 0)
                        {
                            ResetRunningState();
                            return;
                        }

                        if (videoService.GetPeopleNumberAsync(zone).Result > 0)
                        {
                            var detectedImagePath = videoService.IsPoseDetectedAsync(zone).Result;
                            if (!string.IsNullOrWhiteSpace(detectedImagePath))
                            {
                                sampledImages[DateTime.Now.AddTicks(i)] = detectedImagePath;
                            }
                        }

                        Thread.Sleep(timeDelta);
                    }

                    if (sampledImages.Count == 0)
                    {
                        continue;
                    }

                    var sittingImages = sampledImages
                        .Where(x => x.Value.Contains("sitting", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (sittingImages.Count < Math.Ceiling(sampledImages.Count / 2.0))
                    {
                        continue;
                    }

                    var lastSittingImagePath = sittingImages.Last().Value;
                    var selectedImages = await _defectImageService.GetImageWithResultAsync(new List<string> { lastSittingImagePath });
                    var cachedImages = new List<Mat>();

                    try
                    {
                        cachedImages.AddRange(selectedImages);

                        using var grid = DefectImagesSaver.CreateImageGrid(cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "pose");
                        var failure = new PoseFailure()
                        {
                            DateTime = sittingImages.First().Key,
                            DefectImage = new DefectImage() { Path = path }
                        };

                        db.PoseFailures.Add(failure);
                        db.SaveChanges();

                        await _defectImageService.MoveDefectImagesAsync(
                            failure.Name,
                            failure.Id,
                            sampledImages.Values.Distinct().ToList());

                        return;
                    }
                    finally
                    {
                        cachedImages.ForEach(x => x.Dispose());
                    }
                }
            }
            catch (Exception ex)
            {
                ResetRunningState();
                logger?.LogJobError(ex, "PoseClassificationJob");
            }
        }

        private static int GetPeopleNumber(VideoCacheService videoService, List<Zone> zones)
        {
            return zones.Sum(x => videoService.GetPeopleNumberAsync(x).Result);
        }

        private static void ResetRunningState()
        {
            lock (_lock)
            {
                _isRunning = false;
            }
        }
    }
}

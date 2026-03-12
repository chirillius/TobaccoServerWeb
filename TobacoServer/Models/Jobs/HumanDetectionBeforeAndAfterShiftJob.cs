using Newtonsoft.Json;
using OpenCvSharp;
using Quartz;
using System.Diagnostics;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    public class HumanDetectionBeforeAndAfterShiftJob : IJob
    {
        private static object _checkTimesLock = new object();
        private readonly int _intervalInSeconds = 20;
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static object _lock = new object();
        private static List<Mat> _cachedImages = new List<Mat>();

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var t = int.Parse(context.MergedJobDataMap["period"].ToString());
                var _intervalInSeconds = t / 1000;

                var zonesConfigurator = new ZonesConfigurator();
                var cameraAddresses = zonesConfigurator.GetZones().Select(x => x.CameraAddress).Distinct().ToList();
                var zones = new List<Zone>();

                for (int i = 0; i < cameraAddresses.Count; i++)
                {
                    zones.Add(new Zone() { Name = $"Zone{i}", CameraAddress = cameraAddresses[i], Rectangle = new Rectangle() { X = 0, Y = 0, Width = 1920, Height = 1080 } });
                }

                lock (_checkTimesLock)
                {
                    if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds * 3))
                    {
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "bottles");
                        _ = db.HumanDetectionBeforeAndAfterShiftFailures.Add(new HumanDetectionBeforeAndAfterShiftFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last(), DefectImage = new DefectImage() { Path = path } });
                        _ = db.SaveChanges();
                        _checkTimes.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();

                    }

                    if (_checkTimes.Count == 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds * 3))
                    {
                        _checkTimes.Clear();
                        _cachedImages.ForEach(x => x.Dispose());
                        _cachedImages.Clear();
                    }


                    //var a = zones.Any(x => videoService.GetPeopleNumberAsync(x).Result == 0);
                    //if (a)
                    //{
                    //    if (_checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds * 3))
                    //    {
                    //        Debug.WriteLine("added to db");
                    //        db.HumanDetectionBeforeAndAfterShiftFailures.Add(new HumanDetectionBeforeAndAfterShiftFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last() });
                    //        _checkTimes.Clear();
                    //        db.SaveChanges();

                    //    }
                    //    return;
                    //}

                    if (zones.Any(x => videoService.GetPeopleNumberAsync(x).Result >= 1))
                    {
                        if (_checkTimes.Count() > 1 && _cachedImages.Count() == 0)
                        {
                            foreach (var zone in zones)
                            {
                                var image = videoService.TakeShotAsync(zone.CameraAddress).Result;
                                _cachedImages.Add(image);
                            }
                        }
                        //db.ServiceNearCabinetFailures.Add(new ServiceNearCabinetFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last() });
                        _checkTimes.Add(DateTime.Now);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                var logger = context.MergedJobDataMap["logger"] as ILogger;
                logger?.LogError(ex, "Error in HumanDetectionBeforeAndAfterShiftJob");
                throw;
            }
        }
    }
}

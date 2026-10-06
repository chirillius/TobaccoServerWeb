using OpenCvSharp;
using Quartz;
using System.Runtime.CompilerServices;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    public class CrowdControlJob : IJob
    {
        private readonly int _intervalInSeconds = 20;
        private static List<DateTime> _checkTimes = new List<DateTime>();
        public static int _maxPeopleNumber = -1;
        private static object _lock = new object();
        private static List<Mat> _cachedImages = new List<Mat>();

        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope.ServiceProvider.GetService<AppDbContext>();
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
            var interval = (int)context.MergedJobDataMap["interval"] / 1000;
            var zonesConfigurator = new ZonesConfigurator();
            var zones = zonesConfigurator.GetZones();
            var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
            var result = clientZones.Select(x => videoService.GetPeopleNumberAsync(x).Result).Sum();
            lock (_lock)
            {
                if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, 5 * interval))
                {
                    using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                    var path = DefectImagesSaver.Save("Grid", grid, "crowds");

                    _ = db.Crowds.Add(new Crowd()
                    {
                        StartDateTime = _checkTimes.First(),
                        EndDateTime = _checkTimes.Last(),
                        PeopleNumber = _maxPeopleNumber,
                        DefectImage = new DefectImage() { Path = path }
                    });
                    _ = db.SaveChanges();
                    _checkTimes.Clear();
                    _maxPeopleNumber = -1;
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }

                if (_checkTimes.Count == 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, 5 * interval))
                {
                    _checkTimes.Clear();
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }


                if (result > 3)
                {
                    if (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, 5 * interval))
                    {
                        var usedAddresses = new List<string>();
                        _checkTimes.Add(DateTime.Now);
                        if (_maxPeopleNumber < result)
                        {
                            _cachedImages.ForEach(x => x.Dispose());
                            _cachedImages.Clear();
                            foreach (var zone in clientZones)
                            {
                                if (!usedAddresses.Contains(zone.CameraAddress))
                                {
                                    _cachedImages.Add(videoService.TakeShotAsync(zone.CameraAddress).Result);
                                    usedAddresses.Add(zone.CameraAddress);
                                }
                            }
                            _maxPeopleNumber = result;
                        }
                    }
                }
            }
        }
    }
}
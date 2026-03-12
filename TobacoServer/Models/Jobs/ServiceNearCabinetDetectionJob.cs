using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    public class ServiceNearCabinetDetectionJob : IJob
    {
        private readonly int _intervalInSeconds = 20;
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static object _lock = new object();
        private static List<Mat> _cachedImages = new List<Mat>();

        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope.ServiceProvider.GetService<AppDbContext>();
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
            var zoneNamePart = context.MergedJobDataMap["zoneNamePart"].ToString();
            var stallZoneNamePart = context.MergedJobDataMap["stallZoneNamePart"].ToString();
            var t = int.Parse(context.MergedJobDataMap["period"].ToString());
            var _intervalInSeconds = t / 1000 + 10;
            var clientZoneNamePart = context.MergedJobDataMap["clientZoneNamePart"].ToString();
            var zonesConfigurator = new ZonesConfigurator();
            var clientZones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(clientZoneNamePart.ToLower())).ToList();
            var cabinZones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(zoneNamePart.ToLower())).ToList();
            var stallZones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains(stallZoneNamePart.ToLower())).ToList();

            if (cabinZones.Any(x => videoService.GetPeopleNumberAsync(x).Result == 0))
            {
                if (_checkTimes.Count <= 3 && _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds*3))
                {
                    _checkTimes.Clear();
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }

                if (_checkTimes.Count > 3 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds * 3))
                {
                    using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                    var path = DefectImagesSaver.Save("Grid", grid, "serviceNearCabinet");
                    db.ServiceNearCabinetFailures.Add(new ServiceNearCabinetFailure()
                    {
                        StartDateTime = _checkTimes.First(),
                        EndDateTime = _checkTimes.Last(),
                        DefectImage = new DefectImage() { Path = path }
                    });
                    db.SaveChanges();
                    _checkTimes.Clear();
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }
               
            }

            await Task.Delay(10000);

            if (clientZones.Any(x => videoService.GetPeopleNumberAsync(x).Result >= 2))
            {
                if (_checkTimes.Count > 3 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, _intervalInSeconds*3))
                {
                    using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                    var path = DefectImagesSaver.Save("Grid", grid, "serviceNearCabinet");
                    db.ServiceNearCabinetFailures.Add(new ServiceNearCabinetFailure()
                    {
                        StartDateTime = _checkTimes.First(),
                        EndDateTime = _checkTimes.Last(),
                        DefectImage = new DefectImage() { Path = path }
                    });
                    db.SaveChanges();
                }
                _checkTimes.Clear();
                _cachedImages.ForEach(x => x.Dispose());
                _cachedImages.Clear();
                return;
            }

            if (cabinZones.Any(x => videoService.GetPeopleNumberAsync(x).Result == 1)
                && clientZones.Select(x => videoService.GetPeopleNumberAsync(x).Result).Sum() == 1
                && stallZones.Any(x => videoService.GetPeopleNumberAsync(x).Result == 1))
            {
                var usedAddresses = new List<string>();
                _checkTimes.Add(DateTime.Now);

                if (_cachedImages.Any())
                {
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }

                foreach (var zone in cabinZones)
                {
                    if (!usedAddresses.Contains(zone.CameraAddress))
                    {
                        _cachedImages.Add(videoService.TakeShotAsync(zone.CameraAddress).Result);
                        usedAddresses.Add(zone.CameraAddress);
                    }
                }
            }
        }
    }
}

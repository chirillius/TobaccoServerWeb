using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class FoodDetectionJob : IJob
    {
        private static DateTime _timeOfLastWriting = DateTime.Now.AddMinutes(-5);
        private readonly int _intervalInSeconds = 20;
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static object _lock = new object();
        public async Task Execute(IJobExecutionContext context)
        {
            //var logger = context.MergedJobDataMap["logger"] as ILogger;
            //try
            //{
            //    var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            //    var db = dbScope.ServiceProvider.GetService<AppDbContext>();
            //        var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
            //        var interval = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000 + 15;
            //        var zonesConfigurator = new ZonesConfigurator();
            //        var zones = zonesConfigurator.GetZones();
            //        var foodZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
            //        var clientZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).ToList();
            //        var totalClientsNumber = clientZones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();

            //        if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, interval))
            //        {
            //            _ = db.FoodFailures.Add(new FoodFailure() { StartDateTime = _checkTimes.First(), EndDateTime = _checkTimes.Last() });
            //            _ = db.SaveChanges();
            //            _checkTimes.Clear();
            //        }

            //        if (totalClientsNumber <= 0)
            //        {
            //            return;
            //        }

            //        var isDetected = foodZones.Select(async x => await videoService.IsFoodDetectedAsync(x)).Any(x => x.Result == true);

            //        if (totalClientsNumber > 0 && isDetected)
            //        {
            //            lock (_lock)
            //            {
            //                if (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, interval))
            //                {
            //                    _checkTimes.Add(DateTime.Now);
            //                }
            //            }
            //        }
            //    }
            //catch (Exception ex)
            //{
            //    logger.LogJobError(ex, "FoodDetectionJob");
            //}
        }
    }
}

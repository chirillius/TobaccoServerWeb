using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    public class ConversionRegisterJob : IJob
    {
        private static int _peopleCounter = 0;
        private static int _totalPeopleCounter = 0;
        private static List<DateTime> _checkTimes = new List<DateTime>();
        public static int _maxPeopleNumber = -1;
        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var interval = int.Parse(context.MergedJobDataMap["period"].ToString()) / 1000 + 15;
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                var conversionRegisterZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["clientZoneNamePart"].ToString().ToLower())).First();
                var stallZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
                var nextFireTime = context.Trigger.GetNextFireTimeUtc();
                var z = DateTimeOffset.Parse(DateTime.Now.Add(new TimeSpan(1, 0, 0)).ToString());
                if (nextFireTime.Value.ToLocalTime() > z.LocalDateTime)
                {
                    _ = db.ConversionRegister.Add(new ConversionRegister() { DateTime = DateTime.Now.Date, PeopleNumber = _totalPeopleCounter });
                    _ = db.SaveChanges();
                    _totalPeopleCounter = 0;
                }

                if (_checkTimes.Count >= 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, interval))
                {
                    _totalPeopleCounter += _peopleCounter;
                    _peopleCounter = 0;
                    _checkTimes.Clear();
                }

                var stallPeopleNumber = stallZones.Select(async x => await videoService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();

                if (stallPeopleNumber > 0)
                {
                    var clientNumber = await videoService.GetPeopleNumberAsync(conversionRegisterZones);
                    if (clientNumber > 0)
                    {
                        if (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, interval))
                        {
                            _checkTimes.Add(DateTime.Now);
                            if (clientNumber > _peopleCounter)
                            {
                                _peopleCounter = clientNumber;
                            }
                        }
                    }
                }
            }
        }
    }

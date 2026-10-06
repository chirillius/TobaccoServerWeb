using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    internal class CheckPeopleAtStallNumberJob : IJob
    {
        private static object _lock = new object();
        private static List<DateTime> _checkTimes = new List<DateTime>();
        private static int _maxPeopleNumber = -1;
        private static List<DateTime> _noOneAtStallForTooLongCheckTimes = new List<DateTime>();
        private static List<Mat> _cachedTooManyPeopleAtStallImages = new List<Mat>();
        private static List<Mat> _cachedNoOneAtStallImages = new List<Mat>();
        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var interval = (int)context.MergedJobDataMap["interval"] / 1000;
                var zonesConfigurator = new ZonesConfigurator();
                var stallZones = zonesConfigurator.GetZones().Where(x => x.Name.Contains(context.MergedJobDataMap["zoneNamePart"].ToString())).ToList(); ;
                var thresholdInMinutes = int.Parse(context.MergedJobDataMap["thresholdInMinutes"].ToString());
                lock (_lock)
                {
                    var now = DateTime.Now;
                    if (_checkTimes.Count > 1 && DateTime.Now - _checkTimes.Last() > new TimeSpan(0, 0, 5 * interval))
                    {
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedTooManyPeopleAtStallImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "tooManyPeopleAtStall");
                        _ = db.TooManyPeopleAtStallFailures.Add(new TooManyPeopleAtStallFailure()
                        {
                            StartDateTime = _checkTimes.First(),
                            EndDateTime = _checkTimes.Last(),
                            PeopleNumber = _maxPeopleNumber,
                            DefectImage = new DefectImage() { Path = path }
                        });
                        _ = db.SaveChanges();
                        _checkTimes.Clear();
                        _maxPeopleNumber = -1;
                        _cachedTooManyPeopleAtStallImages.ForEach(x => x.Dispose());
                        _cachedTooManyPeopleAtStallImages.Clear();
                    }
                }

                var peopleNumber = 0;
                foreach (var zone in stallZones)
                {
                    peopleNumber += await videoService.GetPeopleNumberAsync(zone);
                }
                if (peopleNumber > 1)
                {
                    lock (_lock)
                    {
                        if (_checkTimes.Count == 0 || _checkTimes.Count > 0 && DateTime.Now - _checkTimes.Last() < new TimeSpan(0, 0, 5 * interval))
                        {
                            _checkTimes.Add(DateTime.Now);
                            if (_maxPeopleNumber < peopleNumber)
                            {
                                _maxPeopleNumber = peopleNumber;
                                _cachedTooManyPeopleAtStallImages.ForEach(x => x.Dispose());
                                _cachedTooManyPeopleAtStallImages.Clear();
                                var usedAddresses = new List<string>();
                                foreach (var zone in stallZones)
                                {
                                    if (!usedAddresses.Contains(zone.CameraAddress))
                                    {
                                        _cachedTooManyPeopleAtStallImages.Add(videoService.TakeShotAsync(zone.CameraAddress).Result);
                                        usedAddresses.Add(zone.CameraAddress);
                                    }
                                }
                            }
                        }
                    }
                }
                if (peopleNumber >= 1)
                {
                    lock (_lock)
                    {
                        if (_noOneAtStallForTooLongCheckTimes.Count > 1 &&
                            _noOneAtStallForTooLongCheckTimes.Last() - _noOneAtStallForTooLongCheckTimes.First() >= new TimeSpan(0, thresholdInMinutes, 0))
                        {
                            using var grid = DefectImagesSaver.CreateImageGrid(_cachedNoOneAtStallImages);
                            var path = DefectImagesSaver.Save("Grid", grid, "noOneAtStallforTooLong");
                            _ = db.NoOneAtStallForTooLongFailures.Add(new NoOneAtStallForTooLongFailure()
                            {
                                StartDateTime = _noOneAtStallForTooLongCheckTimes.First(),
                                EndDateTime = _noOneAtStallForTooLongCheckTimes.Last(),
                                ThresholdInMinutes = thresholdInMinutes.ToString(),
                                DefectImage = new DefectImage() { Path = path }
                            });
                            _ = db.SaveChanges();
                        }
                        _noOneAtStallForTooLongCheckTimes.Clear();
                        _cachedNoOneAtStallImages.ForEach(x => x.Dispose());
                        _cachedNoOneAtStallImages.Clear();
                    }
                }
                else if (peopleNumber == 0)
                {
                    lock (_lock)
                    {

                        if (!_cachedNoOneAtStallImages.Any() && _noOneAtStallForTooLongCheckTimes.Count != 0 && _noOneAtStallForTooLongCheckTimes.Last() - _noOneAtStallForTooLongCheckTimes.First() > new TimeSpan(0, thresholdInMinutes, 0))
                        {
                            var cachedZones = new List<string>();
                            foreach (var stallZone in stallZones)
                            {
                                if (!cachedZones.Contains(stallZone.CameraAddress))
                                {
                                    cachedZones.Add(stallZone.CameraAddress);
                                    _cachedNoOneAtStallImages.Add(videoService.TakeShotAsync(stallZone.CameraAddress).Result);
                                }
                            }
                        }

                        if (_noOneAtStallForTooLongCheckTimes.Count == 0 ||
                            _noOneAtStallForTooLongCheckTimes.Count > 0 && DateTime.Now - _noOneAtStallForTooLongCheckTimes.Last() < new TimeSpan(0, 0, 5 * interval))
                        {
                            _noOneAtStallForTooLongCheckTimes.Add(DateTime.Now);
                        }
                        else
                        {
                            _cachedNoOneAtStallImages.ForEach(x => x.Dispose());
                            _cachedNoOneAtStallImages.Clear();
                            _noOneAtStallForTooLongCheckTimes.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "CheckPeopleAtStallNumberJob");
            }
        }
    }
}

using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class ClothesControlJob : IJob
    {
        private static Queue<bool> _lastResults = new Queue<bool>();
        private static List<Mat> _cachedImages = new List<Mat>();
        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoCacheService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var numberOfIterations = int.Parse(context.MergedJobDataMap["numberOfIterations"].ToString());
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                var clothesControlZones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["zoneNamePart"].ToString().ToLower())).ToList();
                var peopleNumber = clothesControlZones.Select(async x => await videoCacheService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();

                if (peopleNumber > 0)
                {
                    var i = 0;
                    while (i < numberOfIterations)
                    {
                        await Task.Delay(2000);
                        peopleNumber = clothesControlZones.Select(async x => await videoCacheService.GetPeopleNumberAsync(x)).Select(x => x.Result).Sum();

                        if (peopleNumber > 0)
                        {
                            var areClothesBlack = true;
                            var continueNeeded = false;
                            var usedAddresses = new List<string>();
                            foreach (var clothesZone in clothesControlZones)
                            {
                                try
                                {
                                    var result = await videoCacheService.GetClothesColorAsync(clothesZone);
                                    if (!result)
                                    {
                                        areClothesBlack = false;
                                        if (!_cachedImages.Any())
                                        {
                                            foreach (var zone in clothesControlZones)
                                            {
                                                if (!usedAddresses.Contains(zone.CameraAddress))
                                                {
                                                    _cachedImages.Add(videoCacheService.TakeShotAsync(zone.CameraAddress).Result);
                                                    usedAddresses.Add(zone.CameraAddress);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    continueNeeded = true;
                                    return;
                                }
                            }

                            if (continueNeeded)
                            {
                                continue;
                            }

                            _lastResults.Enqueue(areClothesBlack);
                            i++;
                        }
                        await Task.Delay(1000);
                    }


                    if (!(_lastResults.Where(x => x == true).Count() > _lastResults.Count / 2))
                    {
                        using var grid = DefectImagesSaver.CreateImageGrid(_cachedImages);
                        var path = DefectImagesSaver.Save("Grid", grid, "clothes");
                        _ = db.ClothesControlFailures.Add(new ClothesControlFailure()
                        {
                            DateTime = DateTime.Now,
                            DefectImage = new DefectImage() { Path = path }
                        });
                        _ = db.SaveChanges();

                    }
                    _lastResults.Clear();
                    _cachedImages.ForEach(x => x.Dispose());
                    _cachedImages.Clear();
                }
            }


            catch (Exception ex)
            {
                logger.LogJobError(ex, "ClothesControlJob");
                return;
            }
        }
    }
}

using OpenCvSharp;
using Quartz;
using System.Diagnostics;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    internal class DelaysJob : IJob
    {

        /// <summary>
        /// Отправляет запрос на python-сервер, получает полное изображение с выделенными людьми, позу (валидна только при одном человеке в кадре) и список обрезанных изображений со всеми людьми в кадре
        /// </summary>
        /// <returns></returns>
        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();

                //Для анализа опозданий в нейронку отправляются только изображения с зон, в названии которых есть "прилавок", например, "прилавок-1", "прилавок-2" и т.д.
                zones = zones.Where(x => x.Name.ToLower().Contains(context.MergedJobDataMap["ZoneNamePart"].ToString().ToLower())).ToList();

                var hasDelay = true;
                var images = new List<Mat>();
                var usedCameras = new List<string>();
                foreach (var zone in zones)
                {
                    var peopleNumber = await videoService.GetPeopleNumberAsync(zone);
                    if (peopleNumber > 0)
                    {
                        hasDelay = false; //Опоздание засчитывается в случае, когда человека нет ни на одном изображении
                    }
                    else
                    {
                        if (!usedCameras.Contains(zone.CameraAddress))
                        {
                            images.Add(await videoService.TakeShotAsync(zone.CameraAddress));
                            usedCameras.Add(zone.CameraAddress);
                        }
                    }
                }
                if (hasDelay)
                {
                    //foreach (var zone in zones)
                    //{
                    //    var shot = await videoService.GetShotAsync(zone);
                    //    DefectImagesSaver.Save(zone.Name, shot, "DelayDefect");
                    //}
                    using var grid = DefectImagesSaver.CreateImageGrid(images);
                    var path = DefectImagesSaver.Save("Grid", grid, "delays");
                    _ = db.Delays.Add(new Delay() { DateTime = DateTime.Now, DefectImage = new DefectImage() { Path = path } });
                    _ = db.SaveChanges();

                    images.ForEach(x => x.Dispose());
                    images.Clear();
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "DelaysJob");
            }
        }
    }
}

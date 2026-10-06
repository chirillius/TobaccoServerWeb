using Microsoft.EntityFrameworkCore;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Models.ArchiveCutters
{
    public class TimestampDefectCutter<T> : DefectCutter<T> where T : TimestampDefect
    {
        public async Task Execute(IQueryable<T> defects, DateTime date, ILogger logger)
        {

            try
            {
                if (defects is null || defects.Count() == 0)
                {
                    return;
                }
                var currentDefect = defects.First();
                logger.LogInformation($"Вырезание дефекта {currentDefect.Name}");


                var pathToArchive = Path.Combine(Directory.GetCurrentDirectory(), "Videos");
                var neededDateTimes = defects.Where(x => x.DateTime.Date == date.Date).ToList().DistinctBy(x => x.DateTime).Select(x => x.DateTime).ToList();

                await ArchiveHelper.GetCutVideoFromArchive(pathToArchive, neededDateTimes, defects.First().Name);
            }
            catch (Exception ex)
            {
                logger.LogError($"Исключение при вырезании TimeStamp: {ex}");
                return;
            }


        }
    }
}

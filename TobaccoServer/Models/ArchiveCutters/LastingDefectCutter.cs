using TobaccoEntities.Models;

namespace TobacoServer.Models.ArchiveCutters
{
    public class LastingDefectCutter<T> : DefectCutter<T> where T : LastingDefect
    {
        private ILogger _logger;
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
                var neededDateTimes = defects.Where(x => x.StartDateTime.Date == date.Date).ToList().DistinctBy(x => x.StartDateTime)
                               .Select(x => new List<DateTime>() { x.StartDateTime, x.EndDateTime }).ToList();
                var tuples = new List<(DateTime, DateTime)>();

                foreach (var item in neededDateTimes)
                {
                    tuples.Add((item[0], item[1]));
                }
                await ArchiveHelper.GetCutVideoFromArchiveByStartAndEnd(pathToArchive, tuples, defects.First().Name);
            }
            catch (Exception ex)
            {
                logger.LogError($"Исключение при вырезании LastingDefect: {ex}");
                return;
            }
        }
    }
}

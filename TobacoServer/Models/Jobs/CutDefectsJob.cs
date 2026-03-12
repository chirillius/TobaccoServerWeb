using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Services;

namespace TobacoServer.Models.Jobs
{
    public class CutDefectsJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var dbContext = context.MergedJobDataMap["dbContext"] as AppDbContext;
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            var defectCutterService = context.MergedJobDataMap["defectCutterService"] as DefectCutterService;

            logger.LogInformation($"Запущена CutDefectsJob");

            defectCutterService.AddLastingDefectCutter(dbContext.TooManyPeopleAtStallFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.NoOneAtStallForTooLongFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.Crowds.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.HumanDetectionBeforeAndAfterShiftFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.ServiceNearCabinetFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.PhoneFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.CashRegisterFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.ClearStallFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.BottleFailures.Select(x => x as LastingDefect));
            defectCutterService.AddLastingDefectCutter(dbContext.LightFailures.Select(x => x as LastingDefect));


            defectCutterService.AddTimestampDefectCutter(dbContext.PoseFailures.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.Delays.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.SmokeFailures.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.MoppingFailures.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.ClothesControlFailures.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.BadgeFailures.Select(x => x as TimestampDefect));
            defectCutterService.AddTimestampDefectCutter(dbContext.InactiveSalesmanFailures.Select(x => x as TimestampDefect));

            logger.LogInformation($"Добавлены дефекты");


            await defectCutterService.CutAllDefects(DateTime.Now.AddDays(-1));
        }
    }
}

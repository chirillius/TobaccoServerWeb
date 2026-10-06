using Quartz;
using System.Diagnostics;
using TobacoServer.Models.Services;
using static Quartz.Logging.OperationName;

namespace TobacoServer.Models.Jobs
{
    public class NightStoppingJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Debug.WriteLine($"NightStoppingJob was executed at [{DateTime.Now}]");
            var scheduler = context.MergedJobDataMap["scheduler"] as IScheduler;
            var jobs = context.MergedJobDataMap["jobs"] as List<JobKey>;
            foreach (var item in jobs)
            {
                await scheduler.PauseJob(item);
            }
        }
    }
}

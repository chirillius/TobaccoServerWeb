using Quartz;
using System.Diagnostics;

namespace TobacoServer.Models.Jobs
{
    public class DayResumingJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Debug.WriteLine($"DayResumingJob was executed at [{DateTime.Now}]");
            var scheduler = context.MergedJobDataMap["scheduler"] as IScheduler;
            var jobs = context.MergedJobDataMap["jobs"] as List<JobKey>;
            foreach (var item in jobs)
            {
                await scheduler.ResumeJob(item);
            }
        }
    }
}

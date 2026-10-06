using Quartz;
using System.Diagnostics;

namespace TobacoServer.Models.Jobs
{
    public class ResumingJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Debug.WriteLine($"ResumingJob was executed at [{DateTime.Now}]");

            var jobs = context.MergedJobDataMap["jobs"] as List<JobKey>;
            var scheduler = context.MergedJobDataMap["scheduler"] as IScheduler;
            foreach (var item in jobs)
            {
                if ((await scheduler.GetCurrentlyExecutingJobs()).All(x => x.JobDetail.Key != item))
                {
                    await scheduler.ResumeJob(item);
                }
            }
        }
    }
}

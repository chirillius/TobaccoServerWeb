using Quartz;

namespace TobacoServer.Models.Jobs
{
    public interface INightStoppableJob : IJob
    {
        public Task UnloadModel(IJobExecutionContext context);
    }
}

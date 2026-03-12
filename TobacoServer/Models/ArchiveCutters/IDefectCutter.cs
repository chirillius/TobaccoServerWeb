namespace TobacoServer.Models.ArchiveCutters
{
    public interface DefectCutter<T> where T : class
    {
        public Task Execute(IQueryable<T> dbSet, DateTime date, ILogger logger);
        public async Task ExecuteAsync(IQueryable<T> dbSet, DateTime date, ILogger logger)
        {
            await Task.Run(async () =>
            {
                await Execute(dbSet, date, logger);
            });

        }
    }
}

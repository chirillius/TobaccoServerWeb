namespace TobacoServer.Services.Logging
{
    public static class FileLoggerExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath)
        {
            builder.AddProvider(new FileLoggerProvider(filePath));
            return builder;
        }

        public static void LogJobError(this ILogger logger, Exception ex, string jobName)
        {
            logger.LogError(ex, $"Exception was thrown in {jobName}", ex.Message);
        }
    }
}

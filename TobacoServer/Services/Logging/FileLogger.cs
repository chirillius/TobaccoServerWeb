
using Microsoft.Identity.Client;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TobacoServer.Services.Logging
{
    public class FileLogger : ILogger, IDisposable
    {
        private string _filePath;
        private static object _lock = new object();

        public FileLogger(string filePath)
        {
            _filePath = filePath;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return this;
        }

        public void Dispose()
        {

        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                if (logLevel == LogLevel.Error)
                {
                    File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), "error_" + _filePath),
                        $"[{DateTime.Now}] {exception?.Message} " + formatter(state, exception) + Environment.NewLine + exception?.StackTrace + Environment.NewLine);
                }

                if (logLevel == LogLevel.Information)
                {
                    File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), "info_" + _filePath),
                        $"[{DateTime.Now}] {exception?.Message} " + formatter(state, exception) + Environment.NewLine);
                }
            }
        }
    }
}

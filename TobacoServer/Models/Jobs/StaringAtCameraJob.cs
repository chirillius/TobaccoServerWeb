using Quartz;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class StaringAtCameraJob : IJob
    {
        private static int _isRunning = 0;
        private HttpClient _client = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["StaringAtCameraServiceAddress"]) };

        public async Task Execute(IJobExecutionContext context)
        {
            var logger = context.MergedJobDataMap["logger"] as ILogger;
            try
            {
                var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                var clientZonenamePart = context.MergedJobDataMap["clientZoneNamePart"]?.ToString();
                var videoCacheService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
                var cameraAddresses = context.MergedJobDataMap["cameraAddresses"] as List<string>;
                var zones = new ZonesConfigurator().GetZones();
                var clientZones = zones.Where(x => x.Name.Contains(clientZonenamePart)).ToList();
                var clientsNumber = 0;
                if (_isRunning == 0)
                {
                    _ = Interlocked.Exchange(ref _isRunning, 1);

                    foreach (var zone in clientZones)
                    {
                        clientsNumber += await videoCacheService.GetPeopleNumberAsync(zone);
                    }

                    //Если есть - запуск text2speech
                    if (clientsNumber > 0)
                    {
                        var response = await _client.PostAsJsonAsync("/start", new Dictionary<string, List<string>>() { { "cameraAddresses", cameraAddresses } });
                        if (response is null || response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            _ = Interlocked.Exchange(ref _isRunning, 0);
                            throw new Exception("Не удалось запустить staringAtCamera сервер");
                        }
                    }
                }

                else
                {
                    foreach (var zone in clientZones)
                    {
                        clientsNumber += await videoCacheService.GetPeopleNumberAsync(zone);
                    }

                    if (clientsNumber == 0)
                    {
                        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/stop"));
                        if (response is null || response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            _ = Interlocked.Exchange(ref _isRunning, 0);
                            throw new Exception("Не удалось остановить staringAtCamera сервер");
                        }
                        _ = Interlocked.Exchange(ref _isRunning, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogJobError(ex, "SpeechToTextJob");
            }
        }
    }
}

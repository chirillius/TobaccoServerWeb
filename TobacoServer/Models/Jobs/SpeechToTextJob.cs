using Newtonsoft.Json;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;
using TobacoServer.Services.Logging;

namespace TobacoServer.Models.Jobs
{
    public class SpeechToTextJob : IJob
    {
        private static bool _isRunning = false;
        private static bool _isInLock = false;
        private HttpClient _client = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["SpeechToTextAddress"]) };

        public async Task Execute(IJobExecutionContext context)
        {
            //var logger = context.MergedJobDataMap["logger"] as ILogger;
            //try
            //{
            //    var zones = context.MergedJobDataMap["zones"] as List<Zone>;
            //    var clientZonenamePart = context.MergedJobDataMap["clientZoneNamePart"]?.ToString();
            //    var videoCacheService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;
            //    var db = context.MergedJobDataMap["appDbContext"] as AppDbContext;
            //    var cameraAddress = context.MergedJobDataMap["cameraAddress"]?.ToString();
            //    zones = zones.Where(x => x.Name.Contains(clientZonenamePart)).ToList();
            //    var clientsNumber = 0;
            //    var clientsNumbers = new List<int>();

            //    if (_isRunning == 0)
            //    {

            //        _ = Interlocked.Exchange(ref _isRunning, 1);//Сразу выставляем isRunning чтобы другие потоки случайно не запустили 


            //        // Проверить есть ли клиент
            //        foreach (var zone in zones)
            //        {
            //            clientsNumber += await videoCacheService.GetPeopleNumberAsync(zone);
            //        }

            //        //Если есть - запуск text2speech
            //        if (clientsNumber > 0)
            //        {
            //            var response = await _client.PostAsJsonAsync("/start", new Dictionary<string, string>() { { "cameraAddress", cameraAddress } });
            //            if (response is null || response.StatusCode != System.Net.HttpStatusCode.OK)
            //            {
            //                _ = Interlocked.Exchange(ref _isRunning, 0);
            //                throw new Exception("Не удалось запустить speech2text сервер");
            //            }
            //        }
            //    }
            //    //Если нет - остановка text2speech
            //    else
            //    {
            //        for (int i = 0; i < 3; i++)
            //        {
            //            // Проверить есть ли клиент
            //            foreach (var zone in zones)
            //            {
            //                clientsNumber += await videoCacheService.GetPeopleNumberAsync(zone);
            //            }
            //            clientsNumbers.Add(clientsNumber);
            //        }

            //        if (clientsNumbers.All(x => x == 0))
            //        {
            //            var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/stop"));
            //            if (response is null || response.StatusCode != System.Net.HttpStatusCode.OK)
            //            {
            //                _ = Interlocked.Exchange(ref _isRunning, 0);
            //                throw new Exception("Не удалось остановить speech2text сервер");
            //            }
            //            _ = Interlocked.Exchange(ref _isRunning, 0);
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{

            //    logger.LogJobError(ex, "SpeechToTextJob");
            //}

            if (_isInLock)
            {
                return;
            }

            lock (this)
            {
                _isInLock = true;
                var logger = context.MergedJobDataMap["logger"] as ILogger;
                try
                {
                    var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
                    var db = dbScope.ServiceProvider.GetService<AppDbContext>();
                    var zones = context.MergedJobDataMap["zones"] as List<Zone>;
                    var clientZonenamePart = context.MergedJobDataMap["clientZoneNamePart"]?.ToString();
                    var videoCacheService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;

                    var cameraAddress = context.MergedJobDataMap["cameraAddress"]?.ToString();
                    zones = zones.Where(x => x.Name.Contains(clientZonenamePart)).ToList();
                    var clientsNumber = 0;
                    var clientsNumbers = new List<int>();

                    if (_isRunning)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            // Проверить есть ли клиент
                            foreach (var zone in zones)
                            {
                                clientsNumber += videoCacheService.GetPeopleNumberAsync(zone).Result;
                            }
                            clientsNumbers.Add(clientsNumber);
                        }

                        if (clientsNumbers.All(x => x == 0))
                        {
                            var response = _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/stop")).Result;
                            if (response is null || response.StatusCode != System.Net.HttpStatusCode.OK)
                            {
                                _isRunning = false;
                                throw new Exception("Не удалось остановить speech2text сервер");
                            }
                            _isRunning = false;

                        }
                    }
                    else
                    {
                        //Проверить есть ли клиент
                        foreach (var zone in zones)
                        {
                            clientsNumber += videoCacheService.GetPeopleNumberAsync(zone).Result;
                        }

                        //Если есть - запуск text2speech
                        if (clientsNumber > 0)
                        {
                            var response = _client.PostAsJsonAsync("/start", new Dictionary<string, string>()
                            { { "cameraAddress", cameraAddress } }).Result;
                            if (response is null || response.StatusCode != System.Net.HttpStatusCode.NoContent)
                            {
                                throw new Exception("Не удалось запустить speech2text сервер");
                            }
                            _isRunning = true;

                        }
                    }
                }
                catch (Exception ex)
                {
                    _isInLock = false;
                    logger.LogJobError(ex, "SpeechToTextJob");
                }
                _isInLock = false;
            }
        }
    }
}

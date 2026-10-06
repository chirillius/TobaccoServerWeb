using Newtonsoft.Json;

namespace TobacoServer.Services
{
    public class AudioService
    {
        private string _audioServiceAddress;
        private string _audioSource;
        private HttpClient _client;
        ILogger<AudioService> _logger;
        public AudioService(ILogger<AudioService> logger)
        {
            _logger = logger;
            _audioServiceAddress = System.Configuration.ConfigurationManager.AppSettings["RumblingServiceAddress"];
            _audioSource = System.Configuration.ConfigurationManager.AppSettings["AudioSource"];
            _client = new HttpClient() { BaseAddress = new Uri(_audioServiceAddress) };
        }
        public async Task StartServerAsync()
        {
            try
            {
                var responce = await _client.PostAsync("/start", new StringContent(_audioSource));
                if (responce.StatusCode != System.Net.HttpStatusCode.OK || responce.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    throw new Exception("Can't start audio service");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception was thrown while starting audio service");
            }
        }

        public async Task StopServerAsync()
        {
            try
            {
                var responce = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/stop"));
                if (responce.StatusCode != System.Net.HttpStatusCode.OK || responce.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    throw new Exception("Can't stop audio service");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception was thrown while stopping audio service");
            }
        }

        public async Task<List<string>> GetRumblingTimecodesAsync()
        {
            try
            {
                var responce = await _client.GetAsync("/get-timecodes");
                if (responce.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new List<string>();
                }

                if (responce.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Can't get timecodes");
                }

                var json = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(await responce.Content.ReadAsStringAsync());
                return json is not null && json.ContainsKey("timecodes") ? json["timecodes"] : throw new Exception("Can't get timecodes");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception was thrown while getting timecodes");
            }
            return new List<string>();
        }
    }
}

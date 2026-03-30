using Microsoft.AspNetCore.Mvc;
using TobaccoEntities.Models;
using Newtonsoft.Json;
using TobacoServer.Services;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using TobacoServer.Models;

namespace TobacoServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class StoreController : ControllerBase
    {
        private readonly CurrentStoreHandlingService _storeService;
        private readonly IConfiguration _configuration;

        public StoreController(CurrentStoreHandlingService storeService, IConfiguration configuration)
        {
            _storeService = storeService;
            _configuration = configuration;
        }

        [HttpGet("store")]
        public IActionResult GetStore()
        {
            try
            {
                var store = _storeService.GetStore();
                if (store == null || string.IsNullOrEmpty(store.Name) && string.IsNullOrEmpty(store.Address) && (store.Cameras == null || store.Cameras.Count == 0))
                {
                    return NoContent();
                }
                return Ok(store);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка сервера: {ex.Message}");
            }
        }

        [HttpPost("store")]
        public IActionResult SetStore([FromBody] Store store)
        {
            if (store is null || string.IsNullOrEmpty(store.Address))
            {
                return BadRequest("Адрес магазина не может быть null.");
            }
            try
            {
                _storeService.SetStore(store);
                SendAddressToCentralServer(store.Address);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка сервера: {ex.Message}");
            }
        }

        private void SendAddressToCentralServer(string storeAddress)
        {
            try
            {
                using var httpClient = new HttpClient();
                var url = System.Configuration.ConfigurationManager.AppSettings["CentralServer"] + "Stores/check-store";
                var internalApiKey = _configuration["Security:InternalApiKey"];

                if (!string.IsNullOrWhiteSpace(internalApiKey))
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Api-Key", internalApiKey);
                }


                var jsonContent = JsonConvert.SerializeObject(new Store { Address = storeAddress });
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = httpClient.PostAsync(url, httpContent).Result;

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Ошибка при отправке данных на CentralServer: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке данных на CentralServer: {ex.Message}");
            }
        }


        [HttpGet("checkStoreAddress")]

        public IActionResult CheckStoreAddress([FromQuery] string address)
        {
            try
            {
                var store = _storeService.GetStore();

                if (store != null && store.Address == address)
                {
                    return Ok(true);
                }

                return Ok(false);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка сервера: {ex.Message}");
            }
        }

        [HttpPost]
        [Route("check-store-availability")]
        public async Task<IActionResult> CheckStoreAvailability([FromBody] string request)
        {
            try
            {
                string address = request;
                if (string.IsNullOrEmpty(address))
                {
                    return BadRequest("Некорректный адрес магазина.");
                }

                return Ok("TobaccoServer доступен.");
            }
            catch
            {
                return StatusCode(500, "Ошибка при проверке доступности TobaccoServer.");
            }
        }

        [HttpGet("zones")]
        public IActionResult GetZones()
        {
            try
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "zones.json");

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound("File 'zones.json' not found.");
                }

                string fileContent = System.IO.File.ReadAllText(filePath);

                List<Zone> zones = JsonConvert.DeserializeObject<List<Zone>>(fileContent);

                if (zones == null)
                {
                    return BadRequest("Зоны в данном магазине не размечены");
                }

                return Ok(zones);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}

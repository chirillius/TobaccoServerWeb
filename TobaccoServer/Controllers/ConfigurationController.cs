using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using TobaccoEntities.Models;
using TobacoServer.Models;

namespace TobacoServer.Controllers
{

    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        private string _jsonDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["PeriodalTaskJson"]);


        [HttpGet("json-config")]
        public async Task<IActionResult> Get()
        {
            if (!System.IO.File.Exists(_jsonDirectory))
                return NotFound("Config file not found");

            var json = await System.IO.File.ReadAllTextAsync(_jsonDirectory);

            return Content(json, "application/json");
        }



        [HttpPost("json-config")]
        public async Task<IActionResult> Save([FromBody] JsonNode updatedConfig)
        {
            if (updatedConfig == null)
                return BadRequest("Invalid JSON payload");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic)
            };

            var json = updatedConfig.ToJsonString(options);

            await System.IO.File.WriteAllTextAsync(_jsonDirectory, json);

            return Ok(new { message = "Config updated successfully" });
        }

        [HttpPost]
        [Route("syncronize")]
        public async Task<IActionResult> SyncronizeZones([FromBody] Zone zone)
        {
            if (zone is null || string.IsNullOrWhiteSpace(zone.CameraAddress))
            {
                return BadRequest("Некорректные данные зоны.");
            }
            if (!ZoneGeometry.TryNormalize(zone, out var geometryError))
            {
                return BadRequest(geometryError);
            }

            if (!string.IsNullOrWhiteSpace(zone.Name))
            {
                string name = zone.Name.Trim().ToLower();
                zone.Name = char.ToUpper(name[0]) + name[1..];
            }
            else
            {
                return BadRequest("Название зоны не указано.");
            }


            ZonesConfigurator.Synchronize(zone);

            return Ok();
        }


        [HttpDelete("delete-zone")]
        public async Task<IActionResult> DeleteZoneAsync([FromQuery] string zoneName, [FromQuery] string cameraAddress)
        {
            try
            {
                if (!System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "zones.json")))
                    return NotFound("Файла для разметки зон не существует, проверьте магазин");
                if (ZonesConfigurator.RemoveZone(zoneName, cameraAddress)) return Ok();

                return BadRequest("Не удалось удалить зону, пожалуйста, воспользуйтесь ручным удалением или повторите попытку позже");

            }

            catch (System.Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpGet]
        [Route("zone-names")]
        public IActionResult GetZoneNames()
        {
            var zoneNamesPath = Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "zone_names.json");

            if (!System.IO.File.Exists(zoneNamesPath))
            {
                Console.WriteLine("Файл zone_names.json не найден.");
                return NotFound("Файл zone_names.json не найден.");
            }

            var zoneNamesJson = System.IO.File.ReadAllText(zoneNamesPath);
            var zoneNames = JsonConvert.DeserializeObject<List<string>>(zoneNamesJson) ?? new List<string>();

            if (!zoneNames.Any(zoneName => string.Equals(zoneName, "Конверсия", StringComparison.OrdinalIgnoreCase)))
            {
                zoneNames.Add("Конверсия");
            }

            return Content(JsonConvert.SerializeObject(zoneNames), "application/json");
        }



    }
}

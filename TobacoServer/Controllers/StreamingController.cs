using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using System.Diagnostics;
using System.Text;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Services;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class StreamingController : Controller
    {
        private static object _lock = new object();
        private CurrentStoreHandlingService _currentStoreHandlingService;
        private StreamingHandlerService _streamingHandlerService;
        public StreamingController(CurrentStoreHandlingService currentStoreHandlingService, StreamingHandlerService streamingHandlerService)
        {
            _currentStoreHandlingService = currentStoreHandlingService;
            _streamingHandlerService = streamingHandlerService;
        }

        private static string NormalizeQuality(string? quality)
        {
            return string.Equals(quality, "main", StringComparison.OrdinalIgnoreCase)
                ? "main"
                : "secondary";
        }

        [HttpGet]
        [Route("handshake/{id}")]
        public IActionResult Handshake(int id, [FromQuery] string userId, [FromQuery] string? quality)
        {
            var normalizedQuality = NormalizeQuality(quality);
            var key = _streamingHandlerService.CreateEmptySlot(id, _currentStoreHandlingService, normalizedQuality);
            _streamingHandlerService.TakeSlot(id, userId, normalizedQuality);
            return Ok(key);
        }

        [HttpGet]
        [Route("disconnect/{id}")]
        public IActionResult Disconnect(int id, [FromQuery] string userId, [FromQuery] string? quality)
        {
            _streamingHandlerService.ReleaseSlot(id, userId, NormalizeQuality(quality));
            return Ok();
        }

        [HttpGet]
        [Route("{*url}")]
        public async Task<IActionResult> GetTsAsync(string url)
        {
            var parts = url.Split('/');
            var id = int.Parse(parts[0]);
            var quality = NormalizeQuality(parts.Length > 2 ? parts[1] : null);
            var fileName = parts.Last();

            _streamingHandlerService.TouchSlotActivity(id, quality);

            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Playlists", id.ToString(), quality);
            var filePath = Path.Combine(dir, fileName);

            var timeoutMs = 3000;
            var checkIntervalMs = 100;
            int waited = 0;

            while (!System.IO.File.Exists(filePath) && waited < timeoutMs)
            {
                await Task.Delay(checkIntervalMs);
                waited += checkIntervalMs;
            }

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            if (fileName.EndsWith(".m3u8"))
                return PhysicalFile(filePath, "application/vnd.apple.mpegurl", enableRangeProcessing: true);

            if (fileName.EndsWith(".ts"))
                return PhysicalFile(filePath, "video/mp2t", enableRangeProcessing: true);

            return NotFound();
        }
    }
}

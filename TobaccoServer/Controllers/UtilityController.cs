using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using TobacoServer.Services;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class UtilityController : Controller
    {
        public CurrentStoreHandlingService _currentStoreHandlingService { get; set; }
        public UtilityController(CurrentStoreHandlingService currentStoreHandlingService)
        {
            _currentStoreHandlingService = currentStoreHandlingService;
        }

        [HttpGet]
        [Route("get-cameras-previews")]
        public Dictionary<int, byte[]> GetPreviews()
        {
            var cameras = _currentStoreHandlingService.GetStore().Cameras.OrderBy(x => x.Id).ToList();
            var images = new Dictionary<int, byte[]>();
            for (var i = 0; i < cameras.Count; i++)
            {
                using var videoCapture = new VideoCapture(cameras[i].Address);
                byte[] buffer;
                var image = videoCapture.RetrieveMat();
                int j = 0;
                while ((image is null || image.Empty()) && j < 100)
                {
                    image = videoCapture.RetrieveMat();

                }
                if (image is not null && !image.Empty())
                {
                    Cv2.ImEncode(".jpeg", image, out buffer);
                    images.Add(i, buffer);
                    image.Dispose();
                }
            }
            return images;
        }

        [HttpGet]
        [Route("get-camera-preview")]
        public IActionResult GetSingleCameraPreview([FromQuery] string cameraAddress)
        {
            var cameras = _currentStoreHandlingService.GetStore().Cameras;
            var camera = cameras.FirstOrDefault(c => c.Address == cameraAddress);

            if (camera == null)
                return NotFound("Камера не найдена.");

            using var videoCapture = new VideoCapture(camera.Address);
            var image = videoCapture.RetrieveMat();

            var retryCount = 0;
            while ((image == null || image.Empty()) && retryCount < 100)
            {
                image = videoCapture.RetrieveMat();
                retryCount++;
            }

            if (image != null && !image.Empty())
            {
                using var resizedImage = new Mat();
                Cv2.Resize(image, resizedImage, new OpenCvSharp.Size(1920, 1080));
                Cv2.ImEncode(".jpeg", resizedImage, out var buffer);
                image.Dispose();
                return Ok(new { image = Convert.ToBase64String(buffer) });
            }

            return BadRequest("Не удалось получить превью.");
        }

        [HttpGet]
        [Route("get-camera-preview-areas")]
        public IActionResult GetCameraPreview([FromQuery] string cameraAddress)
        {
            var cameras = _currentStoreHandlingService.GetStore().Cameras;
            var camera = cameras.FirstOrDefault(c => c.Address == cameraAddress);
            var images = new Dictionary<int, string>();

            if (camera == null)
                return NotFound("Камера не найдена.");

            for (var i = 0; i < cameras.Count; i++)
            {
                using var videoCapture = new VideoCapture(camera.Address);
                var image = videoCapture.RetrieveMat();

                int retryCount = 0;
                while ((image == null || image.Empty()) && retryCount < 100)
                {
                    image = videoCapture.RetrieveMat();
                    retryCount++;
                }

                if (image != null && !image.Empty())
                {
                    Cv2.ImEncode(".jpeg", image, out var buffer);
                    images.Add(i, Convert.ToBase64String(buffer));
                    image.Dispose();
                }
            }

            if (images.Count > 0)
                return Ok(images);

            return BadRequest("Не удалось получить превью.");
        }



        [HttpGet]
        [Route("get-store")]
        public Store GetStore()
        {
            return _currentStoreHandlingService.GetStore();
        }
    }
}

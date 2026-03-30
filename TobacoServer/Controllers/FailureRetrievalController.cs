using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Newtonsoft.Json;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class FailureRetrievalController : Controller
    {
        protected string _defectImageAddress = System.Configuration.ConfigurationManager.AppSettings["DefectImageServiceAddress"];
        private AppDbContext _db;
        public FailureRetrievalController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        [Route("image")]
        public async Task<IActionResult> GetDefectImage([FromQuery] long globalId)
        {
            var defectImage = await _db.DefectImages.FirstOrDefaultAsync(x => x.Id == globalId);
            if (defectImage is null || string.IsNullOrWhiteSpace(defectImage.Path) || !System.IO.File.Exists(defectImage.Path))
            {
                return NotFound("Изображение не найдено на сервере");
            }

            return PhysicalFile(defectImage.Path, "image/jpeg");
        }

        [HttpPost]
        [Route("images-availability/{defectName}")]
        public IActionResult GetUnavailableImageDates(string defectName, [FromBody] List<string> dates)
        {
            if (dates is null || dates.Count == 0)
            {
                return Ok(new List<string>());
            }

            var defectDirectoryName = defectName.Trim().ToLowerInvariant() switch
            {
                "delays" => "delays",
                "toomanypeopleatstall" => "tooManyPeopleAtStall",
                "smoke" => "smoke",
                "nooneatstallfortoolong" => "noOneAtStallforTooLong",
                "light" => "light",
                "crowd" => "crowds",
                "cashregister" => "cashRegister",
                "countingcashregister" => "countingCashRegister",
                "abandonedopencashregister" => "abandonedOpenCashRegister",
                "humandetectionbeforeandaftershift" => "bottles",
                "servicenearcabinet" => "serviceNearCabinet",
                "phone" => "phones",
                "pose" => "pose",
                "mopping" => "mopping",
                "clothes" => "clothes",
                "surfaceclear" => "clearStall",
                "bottles" => "bottles",
                "inactivesalesman" => "inactiveSalesman",
                "badge" => "badge",
                _ => defectName
            };

            var imagesRoot = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            var unavailableDates = dates
                .Where(date =>
                {
                    var directoryPath = Path.Combine(imagesRoot, date, defectDirectoryName);
                    return !Directory.Exists(directoryPath);
                })
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            return Ok(unavailableDates);
        }

        [HttpPost]
        [Route("false-positive/{defect}-{id}")]
        public async Task<IActionResult> MarkImageAsFalsePositive(string defect, long id)
        {
            HttpClient client = new HttpClient();
            var response = await client.PostAsync($"{_defectImageAddress}images/false-positive/{defect}-{id}", null);
            var message = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(message))
            {
                return StatusCode((int)response.StatusCode);
            }

            return StatusCode((int)response.StatusCode, message);
        }

        [HttpPost]
        [Route("false-positive-export/start")]
        public async Task<IActionResult> StartFalsePositiveExport()
        {
            using var client = new HttpClient();
            var response = await client.PostAsync($"{_defectImageAddress}false-positive-export/start", null);
            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, string.IsNullOrWhiteSpace(message) ? null : message);
            }

            return Content(message, "application/json");
        }

        [HttpGet]
        [Route("false-positive-export/status/{exportId}")]
        public async Task<IActionResult> GetFalsePositiveExportStatus(string exportId)
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"{_defectImageAddress}false-positive-export/status/{exportId}");
            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, string.IsNullOrWhiteSpace(message) ? null : message);
            }

            return Content(message, "application/json");
        }

        [HttpGet]
        [Route("false-positive-export/download/{exportId}")]
        public async Task<IActionResult> DownloadFalsePositiveExport(string exportId)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(
                $"{_defectImageAddress}false-positive-export/download/{exportId}",
                HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                response.Dispose();
                client.Dispose();
                return StatusCode((int)response.StatusCode, string.IsNullOrWhiteSpace(message) ? null : message);
            }

            var stream = await response.Content.ReadAsStreamAsync();
            HttpContext.Response.OnCompleted(async () =>
            {
                stream.Dispose();
                response.Dispose();
                client.Dispose();
                await Task.CompletedTask;
            });

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/zip";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                           ?? response.Content.Headers.ContentDisposition?.FileName
                           ?? $"FalsePositives-{DateTime.Now:dd-MM-yyyy_HH-mm-ss}.zip";
            fileName = fileName.Trim('"');

            return File(stream, contentType, fileName);
        }

        [HttpPost]
        [Route("false-positive-export/finalize/{exportId}")]
        public async Task<IActionResult> FinalizeFalsePositiveExport(string exportId)
        {
            using var client = new HttpClient();
            var response = await client.PostAsync($"{_defectImageAddress}false-positive-export/finalize/{exportId}", null);
            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, string.IsNullOrWhiteSpace(message) ? null : message);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return StatusCode((int)response.StatusCode);
            }

            return Content(message, "application/json");
        }

        [HttpPost]
        [Route("verified/{defect}-{id}")]
        public async Task<IActionResult> MarkDefectAsVerified(string defect, long id)
        {
            var normalizedDefect = defect.Trim().ToLowerInvariant();
            var updated = normalizedDefect switch
            {
                "delay" => await MarkVerifiedAsync(_db.Delays.FirstOrDefaultAsync(x => x.Id == id)),
                "toomanypeopleatstall" => await MarkVerifiedAsync(_db.TooManyPeopleAtStallFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "smoke" => await MarkVerifiedAsync(_db.SmokeFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "nooneatstallfortoolong" => await MarkVerifiedAsync(_db.NoOneAtStallForTooLongFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "light" => await MarkVerifiedAsync(_db.LightFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "crowd" => await MarkVerifiedAsync(_db.Crowds.FirstOrDefaultAsync(x => x.Id == id)),
                "cashregister" => await MarkVerifiedAsync(_db.CashRegisterFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "countingcashregister" => await MarkVerifiedAsync(_db.CountingCashRegisterFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "abandonedopencashregister" => await MarkVerifiedAsync(_db.AbandonedOpenCashRegisterFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "humandetectionbeforeandaftershift" => await MarkVerifiedAsync(_db.HumanDetectionBeforeAndAfterShiftFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "servicenearcabinet" => await MarkVerifiedAsync(_db.ServiceNearCabinetFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "phone" => await MarkVerifiedAsync(_db.PhoneFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "pose" => await MarkVerifiedAsync(_db.PoseFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "mopping" => await MarkVerifiedAsync(_db.MoppingFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "clothes" => await MarkVerifiedAsync(_db.ClothesControlFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "clearstall" => await MarkVerifiedAsync(_db.ClearStallFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "bottle" => await MarkVerifiedAsync(_db.BottleFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "inactivesalesman" => await MarkVerifiedAsync(_db.InactiveSalesmanFailures.FirstOrDefaultAsync(x => x.Id == id)),
                "badge" => await MarkVerifiedAsync(_db.BadgeFailures.FirstOrDefaultAsync(x => x.Id == id)),
                _ => false
            };

            if (!updated)
            {
                return NotFound();
            }

            await _db.SaveChangesAsync();
            using (var client = new HttpClient())
            {
                await client.DeleteAsync($"{_defectImageAddress}images/{defect}-{id}");
            }
            return NoContent();
        }


        [HttpGet]
        [Route("delays")]
        public async Task<List<Delay>> GetDelays([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.Delays.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("too-many-people-at-stall")]
        public async Task<List<TooManyPeopleAtStallFailure>> GetToManyPeopleAtStallFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.TooManyPeopleAtStallFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("smoke")]
        public async Task<List<Smoke>> GetSmokeFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.SmokeFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("no-one-at-stall-for-too-long")]
        public async Task<List<NoOneAtStallForTooLongFailure>> GetNoOneAtStallForTooLong([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.NoOneAtStallForTooLongFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }


        [HttpGet]
        [Route("light")]
        public async Task<List<LightFailure>> GetLightFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.LightFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("crowd")]
        public async Task<List<Crowd>> GetCrowdFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.Crowds.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }


        [HttpGet]
        [Route("cash-register")]
        public async Task<List<CashRegisterFailure>> GetCashRegisterFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.CashRegisterFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("counting-cash-register")]
        public async Task<List<CountingCashRegisterFailure>> GetCountingCashRegisterFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            return await _db.CountingCashRegisterFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).ToListAsync();
        }

        [HttpGet]
        [Route("abandoned-open-cash-register")]
        public async Task<List<AbandonedOpenCashRegisterFailure>> GetAbandonedOpenCashRegisterFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.AbandonedOpenCashRegisterFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("human-detection-before-and-after-shift")]
        public async Task<List<HumanDetectionBeforeAndAfterShiftFailure>> GetHumanDetectionBeforeAndAfterShiftFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            try
            {
                var defects = (await _db.HumanDetectionBeforeAndAfterShiftFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
                return defects;
            }
            catch (Exception)
            {
                throw;
            }

        }

        [HttpGet]
        [Route("service-near-cabinet")]
        public async Task<List<ServiceNearCabinetFailure>> GetServiceNearCabinetFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.ServiceNearCabinetFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("phone")]
        public async Task<List<PhoneFailure>> GetPhoneFailuresAsync([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.PhoneFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }


        [HttpGet]
        [Route("pose")]
        public async Task<List<PoseFailure>> GetPoseFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.PoseFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("mopping")]
        public async Task<List<MoppingFailure>> GetMoppingFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            return await _db.MoppingFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).ToListAsync();
        }

        [HttpGet]
        [Route("clothes")]
        public async Task<List<ClothesControlFailure>> GetClothesFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.ClothesControlFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("surface-clear")]
        public async Task<List<ClearStallFailure>> GetClearStallFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.ClearStallFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("bottles")]
        public async Task<List<BottleFailure>> GetBottlesFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            var defects = (await _db.BottleFailures.Where(x => x.StartDateTime >= startDateTime && x.EndDateTime <= endDateTime).OrderByDescending(x => x.StartDateTime).Include(x => x.DefectImage).ToListAsync());
            defects.ForEach(x => x.DefectImage.Path = "");
            return defects;
        }

        [HttpGet]
        [Route("inactive-salesman")]
        public async Task<List<InactiveSalesmanFailure>> GetInactiveSalesmanFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            return await _db.InactiveSalesmanFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).ToListAsync();
        }

        [HttpGet]
        [Route("badge")]
        public async Task<List<BadgeFailure>> GetBadgeFailures([FromQuery] DateTime startDateTime, [FromQuery] DateTime endDateTime)
        {
            return await _db.BadgeFailures.Where(x => x.DateTime >= startDateTime && x.DateTime <= endDateTime).OrderByDescending(x => x.DateTime).ToListAsync();
        }

        [HttpGet]
        [Route("routes")]
        public async Task<List<string>> GetRoutes()
        {
            var classType = this.GetType();
            var methods = classType.GetMethods();
            var httpGetMethods = methods.Where(x => x.GetCustomAttributes(typeof(HttpGetAttribute), true).Count() > 0).ToList();
            var routes = new List<string>();
            foreach (var method in methods)
            {
                var routeAttribute = method.GetCustomAttribute<RouteAttribute>()?.Template;
                if (!string.IsNullOrEmpty(routeAttribute))
                {
                    routes.Add(routeAttribute);
                }
            }
            return routes;
        }

        public (DateTime startDate, DateTime endDate) ParseDate(string? startDateTime, string? endDateTime)
        {
            var startDate = DateTime.Now.AddDays(-7).Date;
            if (!string.IsNullOrEmpty(startDateTime))
            {
                startDate = DateTime.Parse(startDateTime);
            }
            var endDate = DateTime.Now.Date;
            if (!string.IsNullOrEmpty(endDateTime))
            {
                endDate = DateTime.Parse(endDateTime);
            }
            return (startDate, endDate);
        }

        private static async Task<bool> MarkVerifiedAsync<TDefect>(Task<TDefect?> defectTask) where TDefect : class
        {
            var defect = await defectTask;
            if (defect is null)
            {
                return false;
            }

            var verifiedProperty = defect.GetType().GetProperty("Verified");
            if (verifiedProperty?.PropertyType != typeof(bool) || !verifiedProperty.CanWrite)
            {
                return false;
            }

            verifiedProperty.SetValue(defect, true);
            return true;
        }
    }
}

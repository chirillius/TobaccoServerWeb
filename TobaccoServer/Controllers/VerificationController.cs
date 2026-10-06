using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class VerificationController : Controller
    {
        private AppDbContext _db;
        public VerificationController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [Route("delays")]
        public async Task<IActionResult> PostDelaysFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.Delays.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [Route("too-many-people-at-stall")]
        public async Task<IActionResult> PostTooManyPeopleAtStallFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.TooManyPeopleAtStallFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }


        [HttpPost]
        [Route("smoke")]
        public async Task<IActionResult> PostSmokeFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.SmokeFailures.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [Route("no-one-at-stall-for-too-long")]
        public async Task<IActionResult> PostNoOneAtStallForTooLongFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.NoOneAtStallForTooLongFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("light")]
        public async Task<IActionResult> LightFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.LightFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("crowd")]
        public async Task<IActionResult> PostCrowdFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.Crowds.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }


        [HttpPost]
        [Route("cash-register")]
        public async Task<IActionResult> PostCashRegisterFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.CashRegisterFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("human-detection-before-and-after-shift")]
        public async Task<IActionResult> PostHumanDetectionBefireAndAfterShiftFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.HumanDetectionBeforeAndAfterShiftFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }


        [HttpPost]
        [Route("service-near-cabinet")]
        public async Task<IActionResult> PostServiceNearCabinetFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.ServiceNearCabinetFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("phone")]
        public async Task<IActionResult> PostPhoneFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.PhoneFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }


        [HttpPost]
        [Route("pose")]
        public async Task<IActionResult> PostPoseFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.PoseFailures.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [Route("mopping")]
        public async Task<IActionResult> PostMoppingFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.MoppingFailures.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [Route("clothes")]
        public async Task<IActionResult> PostClothesFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.ClothesControlFailures.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [Route("surface-clear")]
        public async Task<IActionResult> PostSurfaceClearFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.ClearStallFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("bottles")]
        public async Task<IActionResult> PostsBottlesClearFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var startDateTime = DateTime.Parse(json["startDateTime"]);
            var endDateTime = DateTime.Parse(json["endDateTime"]);
            var failure = await _db.BottleFailures.FirstOrDefaultAsync(x => x.StartDateTime == startDateTime && x.EndDateTime == endDateTime);

            if (failure != null)
                if (failure != null)
                {
                    failure.Verified = true;
                    await _db.SaveChangesAsync();
                }
            return Ok();
        }

        [HttpPost]
        [Route("badge")]
        public async Task<IActionResult> PostBadgeFragmentsVerified([FromBody] Dictionary<string, string> json)
        {
            var dateTime = DateTime.Parse(json["dateTime"]);
            var failure = await _db.BadgeFailures.FirstOrDefaultAsync(x => x.DateTime == dateTime);
            if (failure != null)
            {
                failure.Verified = true;
                await _db.SaveChangesAsync();
            }
            return Ok();
        }
    }
}

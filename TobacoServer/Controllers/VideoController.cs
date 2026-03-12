using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class VideoController : Controller
    {
        private AppDbContext _db;
        public VideoController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        [Route("delays")]
        public async Task<List<Delay>> GetDelays([FromQuery] DateTime? dateTimeFilter = null)
        {
            dateTimeFilter ??= DateTime.Now.AddDays(-7).Date;
            return await _db.Delays.Where(x => x.DateTime >= dateTimeFilter).ToListAsync();
        }

        [HttpGet]
        [Route("to-many-people-at-stall")]
        public async Task<IActionResult> GetToManyPeopleAtStallFailures()
        {
            var a = await _db.TooManyPeopleAtStallFailures.ToListAsync();
            return Ok(a);
        }
    }
}

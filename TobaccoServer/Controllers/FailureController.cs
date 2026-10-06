using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using System;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class FailureController : Controller
    {
        private AppDbContext _db;
        public FailureController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [Route("inactive-salesman")]
        public async Task<IActionResult> PostInactiveSalesmanFailure()
        {
            _db.InactiveSalesmanFailures.Add(new InactiveSalesmanFailure() { DateTime = DateTime.Now });
            _db.SaveChanges();
            return Ok();
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class LightController : Controller
    {
        private AppDbContext _db;
        public LightController(AppDbContext db)
        {
            _db = db;
        }
    }
}

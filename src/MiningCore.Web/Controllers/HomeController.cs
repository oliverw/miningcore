using Microsoft.AspNetCore.Mvc;

namespace MiningCore.Controllers
{
    public class PoolController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

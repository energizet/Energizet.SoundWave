using Microsoft.AspNetCore.Mvc;

namespace Energizet.SoundWave.Web.Controllers
{
	public class MainController : Controller
	{
		// GET: Main
		public ActionResult Index()
		{
			return View("Index", "Some text");
		}
	}
}
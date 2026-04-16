using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Job");
}

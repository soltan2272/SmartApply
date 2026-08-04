using System.Diagnostics;
using JobApplicationBot.Models;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Job");

        return View();
    }

    [HttpGet]
    public IActionResult Privacy() => View();

    [HttpGet]
    public IActionResult Terms() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}

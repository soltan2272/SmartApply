using JobApplicationBot.Models;
using JobApplicationBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JobApplicationBot.Controllers;

public class JobController : Controller
{
    private readonly IJobScraperService _scraper;
    private readonly IAiService _ai;
    private readonly IEmailSenderService _emailSender;
    private readonly UserProfile _userProfile;
    private readonly IMemoryCache _cache;

    public JobController(
        IJobScraperService scraper,
        IAiService ai,
        IEmailSenderService emailSender,
        IOptions<UserProfile> userProfile,
        IMemoryCache cache)
    {
        _scraper = scraper;
        _ai = ai;
        _emailSender = emailSender;
        _userProfile = userProfile.Value;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new JobInput());
    }

    [HttpPost]
    public async Task<IActionResult> Analyze(JobInput input)
    {
        if (string.IsNullOrWhiteSpace(input.JobUrl) && string.IsNullOrWhiteSpace(input.JobDescription))
        {
            ModelState.AddModelError("", "Please provide either a job URL or description.");
            return View("Index", input);
        }

        try
        {
            var description = input.JobDescription ?? "";

            if (!string.IsNullOrWhiteSpace(input.JobUrl))
            {
                description = await _scraper.ScrapeJobDescriptionAsync(input.JobUrl);
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                ModelState.AddModelError("", "Could not extract job description. Please paste it manually.");
                return View("Index", input);
            }

            var analysis = await _ai.AnalyzeJobAsync(description);
            var emailPreview = await _ai.GenerateEmailAsync(analysis, _userProfile);

            TempData["Success"] = null;
            return View("Preview", emailPreview);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error analyzing job: {ex.Message}");
            return View("Index", input);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Send(EmailPreviewModel model)
    {
        if (!ModelState.IsValid)
            return View("Preview", model);

        try
        {
            var cvPath = string.IsNullOrEmpty(model.CvPath) ? _userProfile.CvFilePath : model.CvPath;
            await _emailSender.SendEmailAsync(model.ToEmail, model.Subject, model.Body, cvPath);

            TempData["RecipientEmail"] = model.ToEmail;
            TempData["JobTitle"] = model.JobTitle;
            TempData["CompanyName"] = model.CompanyName;
            return RedirectToAction("Success");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Failed to send email: {ex.Message}");
            return View("Preview", model);
        }
    }

    [HttpGet]
    public IActionResult Success()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Search(string? searchId, int page = 1)
    {
        if (!string.IsNullOrEmpty(searchId) && _cache.TryGetValue(searchId, out JobSearchViewModel? cached) && cached != null)
        {
            cached.CurrentPage = page;
            return View(cached);
        }

        return View(new JobSearchViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Search(JobSearchFilter filter)
    {
        var searchId = Guid.NewGuid().ToString("N");
        var viewModel = new JobSearchViewModel
        {
            SearchId = searchId,
            Filter = filter,
            HasSearched = true
        };

        if (string.IsNullOrWhiteSpace(filter.Title))
        {
            ModelState.AddModelError("Filter.Title", "Please enter a job title to search.");
            return View(viewModel);
        }

        try
        {
            viewModel.Results = await _scraper.SearchJobsAsync(
                filter.Title,
                filter.ExperienceLevel,
                filter.DatePosted);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Search failed: {ex.Message}");
        }

        _cache.Set(searchId, viewModel, TimeSpan.FromMinutes(15));
        return View(viewModel);
    }
}

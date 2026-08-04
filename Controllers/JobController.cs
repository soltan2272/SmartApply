using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JobApplicationBot.Controllers;

[Authorize]
public class JobController : Controller
{
    private readonly IJobScraperService _scraper;
    private readonly IAiService _ai;
    private readonly IEmailSenderService _emailSender;
    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IAiQuotaService _quota;
    private readonly IMemoryCache _cache;
    private readonly IApplicationTrackingService _tracking;
    private readonly ICvTextExtractionService _cvText;

    public JobController(
        IJobScraperService scraper,
        IAiService ai,
        IEmailSenderService emailSender,
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials,
        IAiQuotaService quota,
        IMemoryCache cache,
        IApplicationTrackingService tracking,
        ICvTextExtractionService cvText)
    {
        _scraper = scraper;
        _ai = ai;
        _emailSender = emailSender;
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
        _quota = quota;
        _cache = cache;
        _tracking = tracking;
        _cvText = cvText;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<UserProfile?> GetOrPromptProfileAsync(CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return null;

        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.FullName))
        {
            TempData["ProfileIncomplete"] = "Please complete your profile before generating applications.";
            return null;
        }

        return profile;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new JobInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(JobInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.JobUrl) && string.IsNullOrWhiteSpace(input.JobDescription))
        {
            ModelState.AddModelError("", "Please provide either a job URL or description.");
            return View("Index", input);
        }

        var profile = await GetOrPromptProfileAsync(ct);
        if (profile == null)
            return RedirectToAction("Index", "Profile");

        var quota = await _quota.TryConsumeAsync(profile.UserId, ct);
        if (!quota.Allowed)
        {
            ModelState.AddModelError(
                "",
                $"You've used your {quota.Limit} AI analyses this month. Request more tokens or a plan upgrade from Billing.");
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

            var analysis = await _ai.AnalyzeJobAsync(description, null, ct);

            string? cvText = null;
            var cv = await _cvFiles.GetAsync(profile.UserId, ct);
            if (cv != null && cv.Content.Length > 0)
            {
                try
                {
                    cvText = _cvText.ExtractText(cv.Content, cv.FileName, cv.ContentType);
                }
                catch
                {
                    cvText = null;
                }
            }

            var emailPreview = await _ai.GenerateEmailAsync(analysis, profile, null, cvText, ct);
            emailPreview.HasCvOnFile = cv != null;

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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(EmailPreviewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Preview", model);

        var profile = await GetOrPromptProfileAsync(ct);
        if (profile == null)
            return RedirectToAction("Index", "Profile");

        var credential = await _emailCredentials.GetByUserIdAsync(profile.UserId, ct);
        if (credential == null || string.IsNullOrWhiteSpace(credential.Secret))
        {
            ModelState.AddModelError(
                "",
                "Save your Gmail App Password on the Profile page before sending applications. Emails are sent from your Gmail account.");
            return View("Preview", model);
        }

        try
        {
            EmailAttachment? attachment = null;
            var cv = await _cvFiles.GetAsync(profile.UserId, ct);
            if (cv != null && cv.Content.Length > 0)
            {
                attachment = new EmailAttachment(cv.FileName, cv.ContentType, cv.Content);
            }

            var sender = new EmailSenderAccount(
                credential.SenderEmail,
                string.IsNullOrWhiteSpace(credential.SenderName) ? profile.FullName : credential.SenderName,
                credential.Secret!,
                credential.SmtpHost,
                credential.SmtpPort,
                credential.UseStartTls);

            await _emailSender.SendEmailAsync(sender, model.ToEmail, model.Subject, model.Body, attachment);
            if (!string.IsNullOrWhiteSpace(CurrentUserId))
            {
                await _tracking.RecordSingleSendAsync(CurrentUserId!, model, ct);
            }

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

        return View(new JobSearchViewModel
        {
            Filter = new JobSearchFilter { Location = "Egypt" }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search(JobSearchFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Location))
            filter.Location = "Egypt";

        var searchId = $"{CurrentUserId}:{Guid.NewGuid():N}";
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
                filter.DatePosted,
                filter.Location);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Search failed: {ex.Message}");
        }

        _cache.Set(searchId, viewModel, TimeSpan.FromMinutes(15));
        return View(viewModel);
    }
}

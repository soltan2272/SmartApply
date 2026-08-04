using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Billing;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private static readonly string[] AllowedCvExtensions = [".pdf", ".doc", ".docx"];
    private const long MaxCvSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IAiQuotaService _quota;
    private readonly IAiService _ai;
    private readonly ICvTextExtractionService _cvText;
    private readonly ISubscriptionService _subscriptions;
    private readonly UserManager<ApplicationUser> _users;

    public ProfileController(
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials,
        IAiQuotaService quota,
        IAiService ai,
        ICvTextExtractionService cvText,
        ISubscriptionService subscriptions,
        UserManager<ApplicationUser> users)
    {
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
        _quota = quota;
        _ai = ai;
        _cvText = cvText;
        _subscriptions = subscriptions;
        _users = users;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        var cvMeta = await _cvFiles.GetMetadataAsync(userId, ct);
        var emailCredential = await _emailCredentials.GetByUserIdAsync(userId, ct);
        var quota = await _quota.GetStatusAsync(userId, ct);
        var plan = await _subscriptions.GetPlanAsync(userId, ct);

        var vm = new ProfileViewModel
        {
            FullName = profile?.FullName ?? string.Empty,
            Title = profile?.Title ?? string.Empty,
            Phone = profile?.Phone ?? string.Empty,
            ContactEmail = profile?.ContactEmail ?? (await _users.GetUserAsync(User))?.Email ?? string.Empty,
            SkillsSummary = profile?.SkillsSummary ?? string.Empty,
            ExperienceSummary = profile?.ExperienceSummary ?? string.Empty,
            ExistingCvFileName = cvMeta?.FileName,
            ExistingCvSizeBytes = cvMeta?.SizeBytes,
            ExistingCvUploadedAt = cvMeta?.UploadedAt,
            SenderEmail = emailCredential?.SenderEmail
                ?? (await _users.GetUserAsync(User))?.Email
                ?? string.Empty,
            SenderName = emailCredential?.SenderName ?? profile?.FullName ?? string.Empty,
            HasEmailCredential = !string.IsNullOrWhiteSpace(emailCredential?.Secret),
            QuotaUsed = quota.Used,
            QuotaLimit = quota.Limit,
            SubscriptionPlan = plan
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Index(ProfileViewModel vm, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var cvUpdated = false;

        if (vm.CvUpload != null && vm.CvUpload.Length > 0)
        {
            var cvError = await TrySaveCvAsync(userId, vm.CvUpload, ct);
            if (cvError != null)
                ModelState.AddModelError(nameof(vm.CvUpload), cvError);
            else
                cvUpdated = true;
        }

        if (!ModelState.IsValid)
        {
            await PopulateAuxAsync(vm, ct);
            return View(vm);
        }

        var profile = new UserProfile
        {
            UserId = userId,
            FullName = vm.FullName,
            Title = vm.Title,
            Phone = vm.Phone,
            ContactEmail = vm.ContactEmail,
            SkillsSummary = vm.SkillsSummary,
            ExperienceSummary = vm.ExperienceSummary
        };

        await _profiles.UpsertAsync(profile, ct);

        if (!string.IsNullOrWhiteSpace(vm.SenderEmail) || !string.IsNullOrWhiteSpace(vm.SenderAppPassword))
        {
            if (string.IsNullOrWhiteSpace(vm.SenderEmail))
            {
                ModelState.AddModelError(nameof(vm.SenderEmail), "Sender Gmail address is required when saving an App Password.");
                await PopulateAuxAsync(vm, ct);
                return View(vm);
            }

            await _emailCredentials.UpsertAsync(new UserEmailCredential
            {
                UserId = userId,
                Provider = "GmailSmtp",
                SenderEmail = vm.SenderEmail.Trim(),
                SenderName = string.IsNullOrWhiteSpace(vm.SenderName) ? vm.FullName : vm.SenderName.Trim(),
                SmtpHost = "smtp.gmail.com",
                SmtpPort = 587,
                UseStartTls = true,
                Secret = vm.SenderAppPassword
            }, ct);
        }

        TempData["ProfileSaved"] = cvUpdated
            ? "Profile and CV saved. New emails will attach the updated CV."
            : "Profile saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadCv(IFormFile? cvUpload, CancellationToken ct)
    {
        if (cvUpload == null || cvUpload.Length == 0)
        {
            TempData["ProfileIncomplete"] = "Choose a CV file (PDF, DOC, or DOCX) before uploading.";
            return RedirectToAction(nameof(Index));
        }

        var cvError = await TrySaveCvAsync(CurrentUserId, cvUpload, ct);
        if (cvError != null)
        {
            TempData["ProfileIncomplete"] = cvError;
            return RedirectToAction(nameof(Index));
        }

        TempData["ProfileSaved"] =
            $"CV updated: {Path.GetFileName(cvUpload.FileName)}. Click “Fill skills & experience from CV” if you also want profile text refreshed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Cv(CancellationToken ct)
    {
        var cv = await _cvFiles.GetAsync(CurrentUserId, ct);
        if (cv == null)
            return NotFound();

        Response.Headers.CacheControl = "private, no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.ETag = $"\"{cv.UploadedAt.Ticks}-{cv.SizeBytes}\"";
        return File(cv.Content, cv.ContentType, cv.FileName);
    }

    private async Task<string?> TrySaveCvAsync(string userId, IFormFile upload, CancellationToken ct)
    {
        if (upload.Length > MaxCvSizeBytes)
            return $"CV file exceeds the {MaxCvSizeBytes / (1024 * 1024)} MB limit.";

        var ext = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (!AllowedCvExtensions.Contains(ext))
            return $"Allowed file types: {string.Join(", ", AllowedCvExtensions)}.";

        await using var ms = new MemoryStream();
        await upload.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length == 0)
            return "The uploaded CV file was empty.";

        await _cvFiles.UpsertAsync(new UserCvFile
        {
            UserId = userId,
            FileName = Path.GetFileName(upload.FileName),
            ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType,
            SizeBytes = bytes.Length,
            Content = bytes,
            UploadedAt = DateTime.UtcNow
        }, ct);

        return null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FillFromCv(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var profile = await _profiles.GetByUserIdAsync(userId, ct) ?? new UserProfile { UserId = userId };
        var cv = await _cvFiles.GetAsync(userId, ct);
        if (cv == null || cv.Content.Length == 0)
        {
            TempData["ProfileIncomplete"] = "Upload a CV (PDF or DOCX) first, then extract skills and experience.";
            return RedirectToAction(nameof(Index));
        }

        var quota = await _quota.TryConsumeAsync(userId, ct);
        if (!quota.Allowed)
        {
            TempData["ProfileIncomplete"] =
                $"You've used your {quota.Limit} AI analyses this month. Request more tokens or a plan upgrade from Billing.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var cvText = _cvText.ExtractText(cv.Content, cv.FileName, cv.ContentType);
            if (string.IsNullOrWhiteSpace(cvText))
            {
                TempData["ProfileIncomplete"] = "Could not read text from the CV. Try a clearer PDF or DOCX.";
                return RedirectToAction(nameof(Index));
            }

            var extracted = await _ai.ExtractProfileFromCvAsync(cvText, null, ct);

            if (!string.IsNullOrWhiteSpace(extracted.SkillsSummary))
                profile.SkillsSummary = extracted.SkillsSummary;

            var experience = extracted.ExperienceSummary;
            if (string.IsNullOrWhiteSpace(experience))
                experience = profile.ExperienceSummary;

            var nameForRewrite = !string.IsNullOrWhiteSpace(extracted.FullName)
                ? extracted.FullName
                : profile.FullName;

            if (!string.IsNullOrWhiteSpace(experience))
            {
                experience = await _ai.RewriteInFirstPersonAsync(experience, nameForRewrite, null, ct);
                profile.ExperienceSummary = experience;
            }

            if (string.IsNullOrWhiteSpace(profile.FullName) && !string.IsNullOrWhiteSpace(extracted.FullName))
                profile.FullName = extracted.FullName;
            if (string.IsNullOrWhiteSpace(profile.Title) && !string.IsNullOrWhiteSpace(extracted.Title))
                profile.Title = extracted.Title;
            if (string.IsNullOrWhiteSpace(profile.Phone) && !string.IsNullOrWhiteSpace(extracted.Phone))
                profile.Phone = extracted.Phone;
            if (string.IsNullOrWhiteSpace(profile.ContactEmail) && !string.IsNullOrWhiteSpace(extracted.ContactEmail))
                profile.ContactEmail = extracted.ContactEmail;

            await _profiles.UpsertAsync(profile, ct);

            TempData["ProfileSaved"] =
                "Skills and experience were filled from your CV in first person (I / my). Review them below.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ProfileIncomplete"] = $"Could not extract from CV: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RewriteExperience(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.ExperienceSummary))
        {
            TempData["ProfileIncomplete"] = "No experience text found. Fill skills & experience from CV first, or type experience manually.";
            return RedirectToAction(nameof(Index));
        }

        var quota = await _quota.TryConsumeAsync(userId, ct);
        if (!quota.Allowed)
        {
            TempData["ProfileIncomplete"] =
                $"You've used your {quota.Limit} AI analyses this month. Request more tokens or a plan upgrade from Billing.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            profile.ExperienceSummary = await _ai.RewriteInFirstPersonAsync(
                profile.ExperienceSummary,
                profile.FullName,
                null,
                ct);
            await _profiles.UpsertAsync(profile, ct);

            TempData["ProfileSaved"] = "Experience summary was rewritten in first person (I / my).";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ProfileIncomplete"] = $"Could not rewrite experience: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task PopulateAuxAsync(ProfileViewModel vm, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var quota = await _quota.GetStatusAsync(userId, ct);
        vm.QuotaUsed = quota.Used;
        vm.QuotaLimit = quota.Limit;
        vm.SubscriptionPlan = await _subscriptions.GetPlanAsync(userId, ct);

        var meta = await _cvFiles.GetMetadataAsync(userId, ct);
        vm.ExistingCvFileName = meta?.FileName;
        vm.ExistingCvSizeBytes = meta?.SizeBytes;
        vm.ExistingCvUploadedAt = meta?.UploadedAt;

        var emailCredential = await _emailCredentials.GetByUserIdAsync(userId, ct);
        vm.SenderEmail = string.IsNullOrWhiteSpace(vm.SenderEmail) ? emailCredential?.SenderEmail ?? string.Empty : vm.SenderEmail;
        vm.SenderName = string.IsNullOrWhiteSpace(vm.SenderName) ? emailCredential?.SenderName ?? string.Empty : vm.SenderName;
        vm.HasEmailCredential = !string.IsNullOrWhiteSpace(emailCredential?.Secret);
    }
}

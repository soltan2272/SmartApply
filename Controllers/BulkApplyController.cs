using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

[Authorize]
public class BulkApplyController : Controller
{
    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IApplicationTrackingService _tracking;
    private readonly IEmailExtractionService _emailExtraction;
    private readonly ISubscriptionService _subscriptions;

    public BulkApplyController(
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials,
        IApplicationTrackingService tracking,
        IEmailExtractionService emailExtraction,
        ISubscriptionService subscriptions)
    {
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
        _tracking = tracking;
        _emailExtraction = emailExtraction;
        _subscriptions = subscriptions;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await BuildViewModelAsync(new BulkApplyViewModel(), ct, prefillTemplate: true);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtractEmails(BulkApplyViewModel vm, CancellationToken ct)
    {
        var extracted = _emailExtraction.ExtractEmails(vm.SourceText);
        if (extracted.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.SourceText), "No valid email addresses were found in the pasted text.");
            vm = await BuildViewModelAsync(vm, ct);
            return View("Index", vm);
        }

        vm.RecipientEmails = MergeRecipients(vm.RecipientEmails, extracted);
        vm.ExtractedEmailCount = extracted.Count;
        TempData["BulkExtractSuccess"] =
            $"Found {extracted.Count} email{(extracted.Count == 1 ? "" : "s")} in the description. Review the list below, then queue applications.";

        vm = await BuildViewModelAsync(vm, ct);
        return View("Index", vm);
    }

    /// <summary>
    /// Persists the editable Bulk Apply subject/body so they reload on the next visit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(BulkApplyViewModel vm, CancellationToken ct)
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(vm.Subject))
            ModelState.AddModelError(nameof(vm.Subject), "Subject is required to save the template.");
        if (string.IsNullOrWhiteSpace(vm.Body))
            ModelState.AddModelError(nameof(vm.Body), "Email body is required to save the template.");

        if (!ModelState.IsValid)
        {
            vm = await BuildViewModelAsync(vm, ct);
            return View("Index", vm);
        }

        try
        {
            await _profiles.SaveBulkTemplateAsync(CurrentUserId, vm.Subject.Trim(), vm.Body.Trim(), ct);
            TempData["BulkTemplateSaved"] = "Email template saved. It will be loaded automatically next time.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            vm = await BuildViewModelAsync(vm, ct);
            return View("Index", vm);
        }
    }

    /// <summary>
    /// Clears the saved template and restores the profile-based default subject/body.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTemplate(CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(CurrentUserId, ct);
        if (profile == null)
        {
            TempData["BulkTemplateSaved"] = null;
            ModelState.AddModelError(string.Empty, "Complete your profile before resetting the template.");
            var vm = await BuildViewModelAsync(new BulkApplyViewModel(), ct, prefillTemplate: true);
            return View("Index", vm);
        }

        await _profiles.SaveBulkTemplateAsync(CurrentUserId, string.Empty, string.Empty, ct);
        TempData["BulkTemplateSaved"] = "Template reset to the default built from your profile.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(BulkApplyViewModel vm, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        var cv = await _cvFiles.GetAsync(userId, ct);
        var credential = await _emailCredentials.GetByUserIdAsync(userId, ct);

        // Always merge emails found in the pasted description with any manually entered recipients.
        var extracted = _emailExtraction.ExtractEmails(vm.SourceText);
        vm.RecipientEmails = MergeRecipients(vm.RecipientEmails, extracted);
        vm.ExtractedEmailCount = extracted.Count;

        var recipients = ValidateRecipients(vm);
        ValidateTemplate(vm);
        ValidatePrerequisites(profile, cv, credential);

        var limits = await _subscriptions.GetLimitsAsync(userId, ct);
        if (recipients.Count > limits.BulkMaxRecipients)
        {
            ModelState.AddModelError(string.Empty,
                $"Your {limits.Plan} plan allows at most {limits.BulkMaxRecipients} recipients per bulk send. Upgrade on Billing or reduce the list.");
        }

        if (!ModelState.IsValid || profile == null || cv == null || credential == null || string.IsNullOrWhiteSpace(credential.Secret))
        {
            vm = await BuildViewModelAsync(vm, ct);
            return View("Index", vm);
        }

        var subject = vm.Subject.Trim();
        var body = vm.Body.Trim();

        var dispatchId = await _tracking.EnqueueBulkSendAsync(userId, subject, body, recipients, ct);
        return RedirectToAction("Dispatch", "Application", new { id = dispatchId });
    }

    private async Task<BulkApplyViewModel> BuildViewModelAsync(
        BulkApplyViewModel vm,
        CancellationToken ct,
        bool prefillTemplate = false)
    {
        var userId = CurrentUserId;
        var profile = await _profiles.GetByUserIdAsync(userId, ct);
        var cvMeta = await _cvFiles.GetMetadataAsync(userId, ct);
        var credential = await _emailCredentials.GetByUserIdAsync(userId, ct);

        vm.HasCvOnFile = cvMeta != null;
        vm.CvFileName = cvMeta?.FileName;
        vm.HasEmailCredential = !string.IsNullOrWhiteSpace(credential?.Secret);
        vm.HasCompleteProfile = IsProfileComplete(profile);

        var defaultSubject = profile == null ? string.Empty : BuildAutoSubject(profile);
        var defaultBody = profile == null ? string.Empty : BuildAutoBody(profile);
        vm.DefaultSubjectPreview = defaultSubject;
        vm.DefaultBodyPreview = defaultBody;

        var hasSaved = profile != null
            && (!string.IsNullOrWhiteSpace(profile.BulkTemplateSubject)
                || !string.IsNullOrWhiteSpace(profile.BulkTemplateBody));
        vm.HasSavedTemplate = hasSaved;

        if (prefillTemplate || (string.IsNullOrWhiteSpace(vm.Subject) && string.IsNullOrWhiteSpace(vm.Body)))
        {
            if (hasSaved)
            {
                vm.Subject = profile!.BulkTemplateSubject?.Trim() ?? string.Empty;
                vm.Body = profile.BulkTemplateBody?.Trim() ?? string.Empty;
            }
            else
            {
                vm.Subject = defaultSubject;
                vm.Body = defaultBody;
            }
        }

        if (vm.RecipientEmails.Count == 0)
        {
            vm.RecipientEmails.Add(string.Empty);
        }

        return vm;
    }

    private List<string> ValidateRecipients(BulkApplyViewModel vm)
    {
        var emailValidator = new EmailAddressAttribute();
        var recipients = vm.RecipientEmails
            .Select(email => email?.Trim() ?? string.Empty)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            ModelState.AddModelError(string.Empty,
                "Enter at least one recipient email, or paste a description that contains email addresses.");
        }

        if (recipients.Count > BulkApplyViewModel.MaxRecipients)
        {
            ModelState.AddModelError(string.Empty, $"You can send to at most {BulkApplyViewModel.MaxRecipients} recipients at once.");
        }

        foreach (var email in recipients)
        {
            if (!emailValidator.IsValid(email))
            {
                ModelState.AddModelError(string.Empty, $"'{email}' is not a valid email address.");
            }
        }

        return recipients;
    }

    private static List<string> MergeRecipients(IEnumerable<string?> existing, IEnumerable<string> extracted)
    {
        return existing
            .Select(email => email?.Trim() ?? string.Empty)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Concat(extracted)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ValidateTemplate(BulkApplyViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Subject))
        {
            ModelState.AddModelError(nameof(vm.Subject), "Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(vm.Body))
        {
            ModelState.AddModelError(nameof(vm.Body), "Email body is required.");
        }
    }

    private void ValidatePrerequisites(UserProfile? profile, UserCvFile? cv, UserEmailCredential? credential)
    {
        if (!IsProfileComplete(profile))
        {
            ModelState.AddModelError(string.Empty, "Complete your profile title, skills, and experience before using bulk apply.");
        }

        if (cv == null)
        {
            ModelState.AddModelError(string.Empty, "Upload a CV on your Profile page before using bulk apply.");
        }

        if (credential == null || string.IsNullOrWhiteSpace(credential.Secret))
        {
            ModelState.AddModelError(string.Empty, "Save your Gmail App Password on your Profile page before using bulk apply.");
        }
    }

    private static bool IsProfileComplete(UserProfile? profile)
    {
        return profile != null
            && !string.IsNullOrWhiteSpace(profile.FullName)
            && !string.IsNullOrWhiteSpace(profile.Title)
            && !string.IsNullOrWhiteSpace(profile.SkillsSummary)
            && !string.IsNullOrWhiteSpace(profile.ExperienceSummary);
    }

    private static string BuildAutoSubject(UserProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.Title) ? "Opportunity" : profile.Title.Trim();
    }

    private static string BuildAutoBody(UserProfile profile)
    {
        return $"""
            Dear Hiring Team,

            I am interested in this Opportunity.

            {profile.ExperienceSummary}

            My key skills include {profile.SkillsSummary}.

            I have attached my CV for your review.

            Best regards,
            {profile.FullName}
            """;
    }
}

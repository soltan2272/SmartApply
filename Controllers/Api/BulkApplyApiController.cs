using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Billing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/bulk")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class BulkApplyApiController : ControllerBase
{
    private const int MaxRecipients = 500;

    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IApplicationTrackingService _tracking;
    private readonly IEmailExtractionService _emailExtraction;
    private readonly ISubscriptionService _subscriptions;

    public BulkApplyApiController(
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

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("template")]
    public async Task<IActionResult> GetTemplate(CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        var cvMeta = await _cvFiles.GetMetadataAsync(UserId, ct);
        var credential = await _emailCredentials.GetByUserIdAsync(UserId, ct);
        var limits = await _subscriptions.GetLimitsAsync(UserId, ct);

        var hasSaved = profile != null
            && (!string.IsNullOrWhiteSpace(profile.BulkTemplateSubject)
                || !string.IsNullOrWhiteSpace(profile.BulkTemplateBody));

        var subject = hasSaved
            ? profile!.BulkTemplateSubject?.Trim() ?? string.Empty
            : profile == null ? string.Empty : BuildAutoSubject(profile);
        var body = hasSaved
            ? profile!.BulkTemplateBody?.Trim() ?? string.Empty
            : profile == null ? string.Empty : BuildAutoBody(profile);

        return Ok(new BulkTemplateDto
        {
            Subject = subject,
            Body = body,
            HasCvOnFile = cvMeta != null,
            CvFileName = cvMeta?.FileName,
            HasEmailCredential = !string.IsNullOrWhiteSpace(credential?.Secret),
            HasCompleteProfile = IsProfileComplete(profile),
            BulkMaxRecipients = limits.BulkMaxRecipients,
            Plan = limits.Plan
        });
    }

    [HttpPost("extract-emails")]
    public IActionResult ExtractEmails([FromBody] ExtractEmailsRequest request)
    {
        var emails = _emailExtraction.ExtractEmails(request.SourceText ?? string.Empty).ToList();
        return Ok(new ExtractEmailsResponse { Emails = emails, Count = emails.Count });
    }

    [HttpPut("template")]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveBulkTemplateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new ApiErrorResponse { Error = "Subject and body are required." });

        await _profiles.SaveBulkTemplateAsync(UserId, request.Subject.Trim(), request.Body.Trim(), ct);
        return Ok(new { Message = "Email template saved." });
    }

    [HttpDelete("template")]
    public async Task<IActionResult> ResetTemplate(CancellationToken ct)
    {
        await _profiles.SaveBulkTemplateAsync(UserId, string.Empty, string.Empty, ct);
        return Ok(new { Message = "Template reset to profile defaults." });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] BulkSendRequest request, CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        var cv = await _cvFiles.GetAsync(UserId, ct);
        var credential = await _emailCredentials.GetByUserIdAsync(UserId, ct);

        var extracted = _emailExtraction.ExtractEmails(request.SourceText ?? string.Empty);
        var recipients = (request.RecipientEmails ?? [])
            .Select(e => e?.Trim() ?? string.Empty)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Concat(extracted)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var emailValidator = new EmailAddressAttribute();
        if (recipients.Count == 0)
            return BadRequest(new ApiErrorResponse { Error = "Enter at least one recipient email, or paste text that contains emails." });
        if (recipients.Count > MaxRecipients)
            return BadRequest(new ApiErrorResponse { Error = $"You can send to at most {MaxRecipients} recipients at once." });
        foreach (var email in recipients)
        {
            if (!emailValidator.IsValid(email))
                return BadRequest(new ApiErrorResponse { Error = $"'{email}' is not a valid email address." });
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new ApiErrorResponse { Error = "Subject and body are required." });

        if (!IsProfileComplete(profile))
            return BadRequest(new ApiErrorResponse { Error = "Complete your profile title, skills, and experience before using bulk apply." });
        if (cv == null)
            return BadRequest(new ApiErrorResponse { Error = "Upload a CV on your Profile page before using bulk apply." });
        if (credential == null || string.IsNullOrWhiteSpace(credential.Secret))
            return BadRequest(new ApiErrorResponse { Error = "Save your Gmail App Password on your Profile page before using bulk apply." });

        var limits = await _subscriptions.GetLimitsAsync(UserId, ct);
        if (recipients.Count > limits.BulkMaxRecipients)
        {
            return BadRequest(new ApiErrorResponse
            {
                Error = $"Your {limits.Plan} plan allows at most {limits.BulkMaxRecipients} recipients per bulk send."
            });
        }

        var dispatchId = await _tracking.EnqueueBulkSendAsync(
            UserId, request.Subject.Trim(), request.Body.Trim(), recipients, ct);

        return Ok(new BulkSendResponse { DispatchId = dispatchId, RecipientCount = recipients.Count });
    }

    [HttpGet("dispatch/{id:int}")]
    public async Task<IActionResult> GetDispatch(int id, CancellationToken ct)
    {
        var dispatch = await _tracking.GetDispatchAsync(UserId, id, ct);
        if (dispatch == null)
            return NotFound(new ApiErrorResponse { Error = "Dispatch not found." });

        return Ok(new BulkDispatchDto
        {
            Id = dispatch.Id,
            Status = dispatch.Status,
            TotalRecipients = dispatch.TotalRecipients,
            SentCount = dispatch.SentCount,
            FailedCount = dispatch.FailedCount,
            CreatedAt = dispatch.CreatedAt
        });
    }

    private static bool IsProfileComplete(UserProfile? profile) =>
        profile != null
        && !string.IsNullOrWhiteSpace(profile.FullName)
        && !string.IsNullOrWhiteSpace(profile.Title)
        && !string.IsNullOrWhiteSpace(profile.SkillsSummary)
        && !string.IsNullOrWhiteSpace(profile.ExperienceSummary);

    private static string BuildAutoSubject(UserProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Title) ? "Opportunity" : profile.Title.Trim();

    private static string BuildAutoBody(UserProfile profile) =>
        $"""
        Dear Hiring Team,

        I am interested in this Opportunity.

        {profile.ExperienceSummary}

        My key skills include {profile.SkillsSummary}.

        I have attached my CV for your review.

        Best regards,
        {profile.FullName}
        """;
}

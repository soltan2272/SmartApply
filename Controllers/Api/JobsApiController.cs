using System.Security.Claims;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/jobs")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class JobsApiController : ControllerBase
{
    private readonly IJobScraperService _scraper;
    private readonly IAiService _ai;
    private readonly IEmailSenderService _emailSender;
    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IAiQuotaService _quota;
    private readonly IApplicationTrackingService _tracking;
    private readonly ICvTextExtractionService _cvText;
    private readonly JobSearchSettings _jobSearchSettings;
    private readonly AiSettings _aiSettings;

    public JobsApiController(
        IJobScraperService scraper,
        IAiService ai,
        IEmailSenderService emailSender,
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials,
        IAiQuotaService quota,
        IApplicationTrackingService tracking,
        ICvTextExtractionService cvText,
        IOptions<JobSearchSettings> jobSearchSettings,
        IOptions<AiSettings> aiSettings)
    {
        _scraper = scraper;
        _ai = ai;
        _emailSender = emailSender;
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
        _quota = quota;
        _tracking = tracking;
        _cvText = cvText;
        _jobSearchSettings = jobSearchSettings.Value;
        _aiSettings = aiSettings.Value;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] JobSearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new ApiErrorResponse { Error = "Job title is required." });

        var outcome = await _scraper.SearchJobsAsync(
            request.Title, request.ExperienceLevel, request.DatePosted,
            string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim());
        var results = outcome.Results;
        var rankedWithAi = false;
        string? aiRankSkipReason = null;
        var aiConfigured = !string.IsNullOrWhiteSpace(_aiSettings.ApiKey);
        var isAdmin = User.IsInRole("Admin");

        if (request.RankWithAi && results.Count > 0)
        {
            var profile = await _profiles.GetByUserIdAsync(UserId, ct);
            if (profile == null || string.IsNullOrWhiteSpace(profile.FullName))
            {
                aiRankSkipReason = "Complete your profile before using AI ranking.";
            }
            else if (!aiConfigured && string.IsNullOrWhiteSpace(profile.PersonalGeminiApiKey))
            {
                aiRankSkipReason =
                    "Configure AI key in server settings (Ai:ApiKey) to use Rank with AI.";
            }
            else
            {
                var quotaAllowed = isAdmin;
                if (!isAdmin)
                {
                    var quota = await _quota.TryConsumeAsync(UserId, ct);
                    quotaAllowed = quota.Allowed;
                    if (!quota.Allowed)
                    {
                        aiRankSkipReason =
                            $"Monthly AI quota exhausted ({quota.Limit} used). Results are shown without AI ranking.";
                    }
                }

                if (quotaAllowed)
                {
                    try
                    {
                        string? cvText = null;
                        var cv = await _cvFiles.GetAsync(UserId, ct);
                        if (cv is { Content.Length: > 0 })
                        {
                            try { cvText = _cvText.ExtractText(cv.Content, cv.FileName, cv.ContentType); }
                            catch { /* proceed without CV */ }
                        }

                        results = await _ai.RankJobSearchResultsAsync(
                            results, profile, cvText, profile.PersonalGeminiApiKey,
                            _jobSearchSettings.MaxAiRankResults, ct);
                        rankedWithAi = true;
                    }
                    catch (Exception ex)
                    {
                        aiRankSkipReason = $"AI ranking failed: {ex.Message}. Results are shown unranked.";
                    }
                }
            }
        }

        var dtos = results.Select(r => new JobSearchResultDto
        {
            Title = r.Title,
            Company = r.Company,
            Location = r.Location,
            Url = r.Url,
            DatePosted = r.DatePosted,
            LogoUrl = r.LogoUrl,
            Snippet = r.Snippet,
            ResultType = r.ResultType,
            MatchScore = r.MatchScore,
            MatchReason = r.MatchReason
        }).ToList();

        return Ok(new
        {
            items = dtos,
            totalCount = dtos.Count,
            rankedWithAi,
            aiRankSkipReason,
            hiringPostsStatus = outcome.HiringPostsStatus,
            jobAlertCount = dtos.Count(d => !string.Equals(d.ResultType, "Post", StringComparison.OrdinalIgnoreCase)),
            hiringPostCount = dtos.Count(d => string.Equals(d.ResultType, "Post", StringComparison.OrdinalIgnoreCase))
        });
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] JobAnalyzeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.JobUrl) && string.IsNullOrWhiteSpace(request.JobDescription))
            return BadRequest(new ApiErrorResponse { Error = "Provide a job URL or description." });

        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.FullName))
            return BadRequest(new ApiErrorResponse { Error = "Complete your profile before analyzing jobs." });

        var quota = await _quota.TryConsumeAsync(UserId, ct);
        if (!quota.Allowed)
            return BadRequest(new ApiErrorResponse { Error = $"Monthly AI quota exhausted ({quota.Limit} used). Request more tokens." });

        var description = request.JobDescription ?? "";
        if (!string.IsNullOrWhiteSpace(request.JobUrl))
            description = await _scraper.ScrapeJobDescriptionAsync(request.JobUrl);

        if (string.IsNullOrWhiteSpace(description))
            return BadRequest(new ApiErrorResponse { Error = "Could not extract job description." });

        var analysis = await _ai.AnalyzeJobAsync(description, null, ct);

        string? cvText = null;
        var cv = await _cvFiles.GetAsync(UserId, ct);
        if (cv is { Content.Length: > 0 })
        {
            try { cvText = _cvText.ExtractText(cv.Content, cv.FileName, cv.ContentType); }
            catch { /* CV parsing failed, proceed without it */ }
        }

        var emailPreview = await _ai.GenerateEmailAsync(analysis, profile, null, cvText, ct);

        return Ok(new EmailPreviewDto
        {
            ToEmail = emailPreview.ToEmail,
            Subject = emailPreview.Subject,
            Body = emailPreview.Body,
            JobTitle = emailPreview.JobTitle,
            CompanyName = emailPreview.CompanyName,
            MatchedSkills = emailPreview.MatchedSkills,
            MatchScore = emailPreview.MatchScore,
            HasCvOnFile = cv != null
        });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest request, CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        if (profile == null)
            return BadRequest(new ApiErrorResponse { Error = "Complete your profile first." });

        var credential = await _emailCredentials.GetByUserIdAsync(UserId, ct);
        if (credential == null || string.IsNullOrWhiteSpace(credential.Secret))
            return BadRequest(new ApiErrorResponse { Error = "Set your Gmail App Password on the profile page first." });

        EmailAttachment? attachment = null;
        var cv = await _cvFiles.GetAsync(UserId, ct);
        if (cv is { Content.Length: > 0 })
            attachment = new EmailAttachment(cv.FileName, cv.ContentType, cv.Content);

        var sender = new EmailSenderAccount(
            credential.SenderEmail,
            string.IsNullOrWhiteSpace(credential.SenderName) ? profile.FullName : credential.SenderName,
            credential.Secret!,
            credential.SmtpHost,
            credential.SmtpPort,
            credential.UseStartTls);

        await _emailSender.SendEmailAsync(sender, request.ToEmail, request.Subject, request.Body, attachment);

        var model = new Models.EmailPreviewModel
        {
            ToEmail = request.ToEmail,
            Subject = request.Subject,
            Body = request.Body,
            JobTitle = request.JobTitle,
            CompanyName = request.CompanyName,
            MatchedSkills = request.MatchedSkills,
            MatchScore = request.MatchScore
        };
        await _tracking.RecordSingleSendAsync(UserId, model, ct);

        return Ok(new { Message = "Email sent successfully." });
    }
}

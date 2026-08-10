using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/profile")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ProfileApiController : ControllerBase
{
    private static readonly string[] AllowedCvExtensions = [".pdf", ".doc", ".docx"];
    private const long MaxCvSizeBytes = 5 * 1024 * 1024;

    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;
    private readonly IAiService _ai;
    private readonly IAiQuotaService _quota;
    private readonly ICvTextExtractionService _cvText;

    public ProfileApiController(
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials,
        IAiService ai,
        IAiQuotaService quota,
        ICvTextExtractionService cvText)
    {
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
        _ai = ai;
        _quota = quota;
        _cvText = cvText;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        var cvMeta = await _cvFiles.GetMetadataAsync(UserId, ct);
        var cred = await _emailCredentials.GetByUserIdAsync(UserId, ct);

        var dto = new UserProfileDto
        {
            FullName = profile?.FullName ?? string.Empty,
            Title = profile?.Title ?? string.Empty,
            Phone = profile?.Phone ?? string.Empty,
            ContactEmail = profile?.ContactEmail ?? string.Empty,
            SkillsSummary = profile?.SkillsSummary ?? string.Empty,
            ExperienceSummary = profile?.ExperienceSummary ?? string.Empty,
            BulkTemplateSubject = profile?.BulkTemplateSubject,
            BulkTemplateBody = profile?.BulkTemplateBody,
            CvFile = cvMeta != null ? new CvFileMetadataDto
            {
                FileName = cvMeta.FileName,
                ContentType = cvMeta.ContentType,
                SizeBytes = cvMeta.SizeBytes,
                UploadedAt = cvMeta.UploadedAt
            } : null,
            EmailCredential = cred != null ? new EmailCredentialDto
            {
                SenderEmail = cred.SenderEmail,
                SenderName = cred.SenderName,
                SmtpHost = cred.SmtpHost,
                SmtpPort = cred.SmtpPort,
                HasSecret = !string.IsNullOrWhiteSpace(cred.Secret)
            } : null
        };

        return Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var existing = await _profiles.GetByUserIdAsync(UserId, ct);

        var profile = existing ?? new UserProfile { UserId = UserId };
        profile.FullName = request.FullName;
        profile.Title = request.Title;
        profile.Phone = request.Phone;
        profile.ContactEmail = request.ContactEmail;
        profile.SkillsSummary = request.SkillsSummary;
        profile.ExperienceSummary = request.ExperienceSummary;
        profile.UpdatedAt = DateTime.UtcNow;

        await _profiles.UpsertAsync(profile, ct);
        return Ok(new { Message = "Profile updated." });
    }

    [HttpPost("cv")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadCv(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiErrorResponse { Error = "No file uploaded." });

        if (file.Length > MaxCvSizeBytes)
            return BadRequest(new ApiErrorResponse { Error = "File exceeds 5 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedCvExtensions.Contains(ext))
            return BadRequest(new ApiErrorResponse { Error = $"Allowed extensions: {string.Join(", ", AllowedCvExtensions)}" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var cvFile = new UserCvFile
        {
            UserId = UserId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Content = ms.ToArray(),
            UploadedAt = DateTime.UtcNow
        };

        await _cvFiles.UpsertAsync(cvFile, ct);
        return Ok(new { Message = "CV uploaded.", FileName = file.FileName, SizeBytes = file.Length });
    }

    [HttpDelete("cv")]
    public async Task<IActionResult> DeleteCv(CancellationToken ct)
    {
        await _cvFiles.DeleteAsync(UserId, ct);
        return Ok(new { Message = "CV deleted." });
    }

    [HttpPut("email-credential")]
    public async Task<IActionResult> UpdateEmailCredential([FromBody] UpdateEmailCredentialRequest request, CancellationToken ct)
    {
        var existing = await _emailCredentials.GetByUserIdAsync(UserId, ct);

        var cred = existing ?? new UserEmailCredential { UserId = UserId };
        cred.SenderEmail = request.SenderEmail;
        cred.SenderName = request.SenderName;
        cred.SmtpHost = request.SmtpHost;
        cred.SmtpPort = request.SmtpPort;
        cred.UseStartTls = request.UseStartTls;
        if (!string.IsNullOrWhiteSpace(request.Secret))
            cred.Secret = request.Secret;
        cred.UpdatedAt = DateTime.UtcNow;

        await _emailCredentials.UpsertAsync(cred, ct);
        return Ok(new { Message = "Email credential updated." });
    }

    [HttpPost("fill-from-cv")]
    public async Task<IActionResult> FillFromCv(CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct) ?? new UserProfile { UserId = UserId };
        var cv = await _cvFiles.GetAsync(UserId, ct);
        if (cv == null || cv.Content.Length == 0)
            return BadRequest(new ApiErrorResponse { Error = "Upload a CV (PDF or DOCX) first, then extract skills and experience." });

        var quota = await _quota.TryConsumeAsync(UserId, ct);
        if (!quota.Allowed)
        {
            return BadRequest(new ApiErrorResponse
            {
                Error = $"You've used your {quota.Limit} AI analyses this month. Request more tokens or a plan upgrade from Billing."
            });
        }

        try
        {
            var cvText = _cvText.ExtractText(cv.Content, cv.FileName, cv.ContentType);
            if (string.IsNullOrWhiteSpace(cvText))
                return BadRequest(new ApiErrorResponse { Error = "Could not read text from the CV. Try a clearer PDF or DOCX." });

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

            profile.UpdatedAt = DateTime.UtcNow;
            await _profiles.UpsertAsync(profile, ct);

            return Ok(new UserProfileDto
            {
                FullName = profile.FullName,
                Title = profile.Title,
                Phone = profile.Phone,
                ContactEmail = profile.ContactEmail,
                SkillsSummary = profile.SkillsSummary,
                ExperienceSummary = profile.ExperienceSummary
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiErrorResponse { Error = $"Could not extract from CV: {ex.Message}" });
        }
    }

    [HttpPost("rewrite-experience")]
    public async Task<IActionResult> RewriteExperience(CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(UserId, ct);
        if (profile == null || string.IsNullOrWhiteSpace(profile.ExperienceSummary))
        {
            return BadRequest(new ApiErrorResponse
            {
                Error = "No experience text found. Fill from CV first, or type experience manually."
            });
        }

        var quota = await _quota.TryConsumeAsync(UserId, ct);
        if (!quota.Allowed)
        {
            return BadRequest(new ApiErrorResponse
            {
                Error = $"You've used your {quota.Limit} AI analyses this month. Request more tokens or a plan upgrade from Billing."
            });
        }

        try
        {
            profile.ExperienceSummary = await _ai.RewriteInFirstPersonAsync(
                profile.ExperienceSummary, profile.FullName, null, ct);
            profile.UpdatedAt = DateTime.UtcNow;
            await _profiles.UpsertAsync(profile, ct);

            return Ok(new { ExperienceSummary = profile.ExperienceSummary });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiErrorResponse { Error = $"Could not rewrite experience: {ex.Message}" });
        }
    }
}

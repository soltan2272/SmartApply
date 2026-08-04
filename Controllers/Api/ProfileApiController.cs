using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ProfileApiController : ControllerBase
{
    private static readonly string[] AllowedCvExtensions = [".pdf", ".doc", ".docx"];
    private const long MaxCvSizeBytes = 5 * 1024 * 1024;

    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _emailCredentials;

    public ProfileApiController(
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository emailCredentials)
    {
        _profiles = profiles;
        _cvFiles = cvFiles;
        _emailCredentials = emailCredentials;
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
}

using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Models.Api;

public class UserProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string SkillsSummary { get; set; } = string.Empty;
    public string ExperienceSummary { get; set; } = string.Empty;
    public string? BulkTemplateSubject { get; set; }
    public string? BulkTemplateBody { get; set; }
    public CvFileMetadataDto? CvFile { get; set; }
    public EmailCredentialDto? EmailCredential { get; set; }
}

public class UpdateProfileRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(256), EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string SkillsSummary { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string ExperienceSummary { get; set; } = string.Empty;
}

public class CvFileMetadataDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class EmailCredentialDto
{
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public bool HasSecret { get; set; }
}

public class UpdateEmailCredentialRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string SenderEmail { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SenderName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    [MaxLength(200)]
    public string? Secret { get; set; }
}

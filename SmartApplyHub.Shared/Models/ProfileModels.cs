namespace SmartApplyHub.Shared.Models;

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
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string SkillsSummary { get; set; } = string.Empty;
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
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Secret { get; set; }
}

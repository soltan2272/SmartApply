namespace JobApplicationBot.Models;

public class AiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3-flash-preview";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

public class EmailSettings
{
    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderPassword { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}

public class UserProfile
{
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string CvFilePath { get; set; } = string.Empty;
    public string SkillsSummary { get; set; } = string.Empty;
    public string ExperienceSummary { get; set; } = string.Empty;
}

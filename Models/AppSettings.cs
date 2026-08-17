namespace JobApplicationBot.Models;

public class AiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-1.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int FreeQuotaPerMonth { get; set; } = 20;
}

public class EmailSettings
{
    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderPassword { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}

public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string ProPriceId { get; set; } = string.Empty;
    public string PowerPriceId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(ProPriceId)
        && !string.IsNullOrWhiteSpace(PowerPriceId);
}

public class SubscriptionSettings
{
    public int FreeAiPerMonth { get; set; } = 20;
    public int ProAiPerMonth { get; set; } = 200;
    public int PowerAiPerMonth { get; set; } = 500;
    public int FreeBulkMaxRecipients { get; set; } = 10;
    public int ProBulkMaxRecipients { get; set; } = 200;
    public int PowerBulkMaxRecipients { get; set; } = 500;
}

public class DataProtectionSettings
{
    public string KeysPath { get; set; } = "keys";
}

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SmartApplyHub";
    public string Audience { get; set; } = "SmartApplyHub.Mobile";
    public int ExpiryMinutes { get; set; } = 1440;
}

public class JobSearchSettings
{
    /// <summary>How many results to send to Gemini when ranking a search (batched).</summary>
    public int MaxAiRankResults { get; set; } = 50;
}

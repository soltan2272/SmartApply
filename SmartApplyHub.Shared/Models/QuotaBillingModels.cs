namespace SmartApplyHub.Shared.Models;

public class QuotaStatusDto
{
    public int Used { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public string Plan { get; set; } = string.Empty;
}

public class SubscriptionRequestDto
{
    public string Plan { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class TokenResetRequestDto
{
    public string? Note { get; set; }
}

public class RequestStatusDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RequestedPlan { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class PlanInfoDto
{
    public string Plan { get; set; } = string.Empty;
    public int AiPerMonth { get; set; }
    public int BulkMaxRecipients { get; set; }
    public decimal MonthlyPriceUsd { get; set; }
}

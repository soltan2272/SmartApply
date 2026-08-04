using JobApplicationBot.Services.Billing;

namespace JobApplicationBot.Models;

public class BillingPageViewModel
{
    public string CurrentPlan { get; set; } = "Free";
    public List<PlanLimits> Plans { get; set; } = [];
    public bool StripeConfigured { get; set; }
    public string? PublishableKey { get; set; }

    public int QuotaUsed { get; set; }
    public int QuotaLimit { get; set; }
    public bool HasPendingSubscriptionRequest { get; set; }
    public string? PendingSubscriptionPlan { get; set; }
    public bool HasPendingTokenResetRequest { get; set; }
}

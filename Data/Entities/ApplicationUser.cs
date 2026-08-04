using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public UserProfile? Profile { get; set; }

    /// <summary>Free, Pro, or Power.</summary>
    [MaxLength(20)]
    public string SubscriptionPlan { get; set; } = SubscriptionPlans.Free;

    [MaxLength(100)]
    public string? StripeCustomerId { get; set; }

    [MaxLength(100)]
    public string? StripeSubscriptionId { get; set; }

    public DateTime? SubscriptionUpdatedAt { get; set; }

    /// <summary>UTC time of last successful login.</summary>
    public DateTime? LastLoginAt { get; set; }
}

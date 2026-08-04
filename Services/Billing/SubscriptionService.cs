using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JobApplicationBot.Services.Billing;

public record PlanLimits(string Plan, int AiPerMonth, int BulkMaxRecipients, decimal MonthlyPriceUsd);

public interface ISubscriptionService
{
    Task<string> GetPlanAsync(string userId, CancellationToken ct = default);
    Task<PlanLimits> GetLimitsAsync(string userId, CancellationToken ct = default);
    PlanLimits GetLimitsForPlan(string plan);
    Task SetPlanAsync(string userId, string plan, string? stripeCustomerId = null, string? stripeSubscriptionId = null, CancellationToken ct = default);
    IReadOnlyList<PlanLimits> GetPublicPlans();
}

public class SubscriptionService : ISubscriptionService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SubscriptionSettings _settings;

    public SubscriptionService(UserManager<ApplicationUser> users, IOptions<SubscriptionSettings> settings)
    {
        _users = users;
        _settings = settings.Value;
    }

    public async Task<string> GetPlanAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        return SubscriptionPlans.Normalize(user?.SubscriptionPlan);
    }

    public async Task<PlanLimits> GetLimitsAsync(string userId, CancellationToken ct = default)
    {
        var plan = await GetPlanAsync(userId, ct);
        return GetLimitsForPlan(plan);
    }

    public PlanLimits GetLimitsForPlan(string plan)
    {
        plan = SubscriptionPlans.Normalize(plan);
        return plan switch
        {
            SubscriptionPlans.Pro => new PlanLimits(
                SubscriptionPlans.Pro,
                _settings.ProAiPerMonth,
                _settings.ProBulkMaxRecipients,
                14m),
            SubscriptionPlans.Power => new PlanLimits(
                SubscriptionPlans.Power,
                _settings.PowerAiPerMonth,
                _settings.PowerBulkMaxRecipients,
                35m),
            _ => new PlanLimits(
                SubscriptionPlans.Free,
                _settings.FreeAiPerMonth,
                _settings.FreeBulkMaxRecipients,
                0m)
        };
    }

    public async Task SetPlanAsync(
        string userId,
        string plan,
        string? stripeCustomerId = null,
        string? stripeSubscriptionId = null,
        CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User was not found.");

        user.SubscriptionPlan = SubscriptionPlans.Normalize(plan);
        user.SubscriptionUpdatedAt = DateTime.UtcNow;
        if (stripeCustomerId != null)
            user.StripeCustomerId = stripeCustomerId;
        if (stripeSubscriptionId != null)
            user.StripeSubscriptionId = stripeSubscriptionId;

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public IReadOnlyList<PlanLimits> GetPublicPlans() =>
    [
        GetLimitsForPlan(SubscriptionPlans.Free),
        GetLimitsForPlan(SubscriptionPlans.Pro),
        GetLimitsForPlan(SubscriptionPlans.Power)
    ];
}

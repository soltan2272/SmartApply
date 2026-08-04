using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Services.Billing;

namespace JobApplicationBot.Services.Quota;

public class AiQuotaService : IAiQuotaService
{
    private readonly IAiUsageRepository _usage;
    private readonly ISubscriptionService _subscriptions;

    public AiQuotaService(IAiUsageRepository usage, ISubscriptionService subscriptions)
    {
        _usage = usage;
        _subscriptions = subscriptions;
    }

    public async Task<QuotaCheckResult> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var limits = await _subscriptions.GetLimitsAsync(userId, ct);
        var used = await _usage.GetCurrentMonthCountAsync(userId, ct);
        return new QuotaCheckResult(used < limits.AiPerMonth, used, limits.AiPerMonth);
    }

    public async Task<QuotaCheckResult> TryConsumeAsync(string userId, CancellationToken ct = default)
    {
        var limits = await _subscriptions.GetLimitsAsync(userId, ct);
        var newCount = await _usage.TryIncrementIfBelowLimitAsync(userId, limits.AiPerMonth, ct);

        if (newCount.HasValue)
            return new QuotaCheckResult(true, newCount.Value, limits.AiPerMonth);

        var used = await _usage.GetCurrentMonthCountAsync(userId, ct);
        return new QuotaCheckResult(false, used, limits.AiPerMonth);
    }

    public Task ResetTokensAsync(string userId, CancellationToken ct = default)
        => _usage.ResetCurrentMonthAsync(userId, ct);
}

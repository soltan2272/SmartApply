namespace JobApplicationBot.Services.Quota;

public record QuotaCheckResult(bool Allowed, int Used, int Limit, bool BypassedByPersonalKey = false)
{
    public int Remaining => Math.Max(0, Limit - Used);
}

public interface IAiQuotaService
{
    Task<QuotaCheckResult> GetStatusAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Atomically checks the quota and increments usage if allowed.
    /// </summary>
    Task<QuotaCheckResult> TryConsumeAsync(string userId, CancellationToken ct = default);

    Task ResetTokensAsync(string userId, CancellationToken ct = default);
}

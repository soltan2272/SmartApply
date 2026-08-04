namespace JobApplicationBot.Data.Repositories;

public interface IAiUsageRepository
{
    Task<int> GetCurrentMonthCountAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the user's monthly counter only if it is strictly less than <paramref name="limit"/>.
    /// Returns the new count when the increment succeeded, or null when the limit was already reached.
    /// </summary>
    Task<int?> TryIncrementIfBelowLimitAsync(string userId, int limit, CancellationToken ct = default);

    /// <summary>Resets the current UTC month usage counter to 0 for the user.</summary>
    Task ResetCurrentMonthAsync(string userId, CancellationToken ct = default);
}

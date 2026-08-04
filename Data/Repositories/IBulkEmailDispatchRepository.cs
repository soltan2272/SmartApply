using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Data.Repositories;

public interface IBulkEmailDispatchRepository
{
    Task<BulkEmailDispatch> AddAsync(BulkEmailDispatch dispatch, CancellationToken ct = default);
    Task<BulkEmailDispatch?> GetByIdAsync(int id, string userId, CancellationToken ct = default);
    Task<BulkEmailDispatch?> GetByIdForProcessingAsync(int id, CancellationToken ct = default);
    Task UpdateAsync(BulkEmailDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims the next claimable dispatch (Queued, or Processing with expired lease).
    /// Returns null when nothing is available.
    /// </summary>
    Task<BulkEmailDispatch?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>Extends the lease while a long send is in progress.</summary>
    Task RenewLeaseAsync(int dispatchId, string workerId, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Re-queues dispatches that were marked Completed while recipient rows were still Queued.
    /// </summary>
    Task<int> RequeueStuckDispatchesAsync(CancellationToken ct = default);
}

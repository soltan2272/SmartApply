using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class BulkEmailDispatchRepository : IBulkEmailDispatchRepository
{
    private readonly ApplicationDbContext _db;

    public BulkEmailDispatchRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BulkEmailDispatch> AddAsync(BulkEmailDispatch dispatch, CancellationToken ct = default)
    {
        _db.BulkEmailDispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);
        return dispatch;
    }

    public Task<BulkEmailDispatch?> GetByIdAsync(int id, string userId, CancellationToken ct = default)
    {
        return _db.BulkEmailDispatches
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
    }

    public Task<BulkEmailDispatch?> GetByIdForProcessingAsync(int id, CancellationToken ct = default)
    {
        return _db.BulkEmailDispatches
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task UpdateAsync(BulkEmailDispatch dispatch, CancellationToken ct = default)
    {
        _db.BulkEmailDispatches.Update(dispatch);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<BulkEmailDispatch?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // UPDLOCK + READPAST: skip rows locked by other workers; claim one safely.
            var candidates = await _db.BulkEmailDispatches
                .FromSqlRaw("""
                    SELECT TOP (1) *
                    FROM BulkEmailDispatches WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE Status = {0}
                       OR (Status = {1} AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt < {2}))
                    ORDER BY CreatedAt
                    """,
                    BulkEmailDispatchStatus.Queued,
                    BulkEmailDispatchStatus.Processing,
                    now)
                .ToListAsync(ct);

            var dispatch = candidates.FirstOrDefault();
            if (dispatch == null)
            {
                await tx.CommitAsync(ct);
                return null;
            }

            dispatch.Status = BulkEmailDispatchStatus.Processing;
            dispatch.LeaseOwner = workerId;
            dispatch.LeaseExpiresAt = leaseUntil;
            dispatch.StartedAt ??= now;
            dispatch.CompletedAt = null;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return dispatch;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task RenewLeaseAsync(int dispatchId, string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.BulkEmailDispatches
            .Where(d => d.Id == dispatchId && d.LeaseOwner == workerId && d.Status == BulkEmailDispatchStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LeaseExpiresAt, now.Add(leaseDuration)), ct);
    }

    public async Task<int> RequeueStuckDispatchesAsync(CancellationToken ct = default)
    {
        var stuckDispatchIds = await _db.JobApplications
            .AsNoTracking()
            .Where(a => a.BulkDispatchId != null && a.Status == JobApplicationStatus.Queued)
            .Select(a => a.BulkDispatchId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (stuckDispatchIds.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        var stuck = await _db.BulkEmailDispatches
            .Where(d => stuckDispatchIds.Contains(d.Id)
                && d.Status == BulkEmailDispatchStatus.Completed)
            .ToListAsync(ct);

        // Also reclaim processing jobs whose lease expired and still have pending recipients.
        var expiredProcessing = await _db.BulkEmailDispatches
            .Where(d => stuckDispatchIds.Contains(d.Id)
                && d.Status == BulkEmailDispatchStatus.Processing
                && d.LeaseExpiresAt != null
                && d.LeaseExpiresAt < now)
            .ToListAsync(ct);

        var toFix = stuck.Concat(expiredProcessing).DistinctBy(d => d.Id).ToList();
        foreach (var dispatch in toFix)
        {
            dispatch.Status = BulkEmailDispatchStatus.Queued;
            dispatch.LeaseOwner = null;
            dispatch.LeaseExpiresAt = null;
            dispatch.CompletedAt = null;
        }

        if (toFix.Count > 0)
            await _db.SaveChangesAsync(ct);

        return toFix.Count;
    }
}

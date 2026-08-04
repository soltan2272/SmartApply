using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class JobApplicationRepository : IJobApplicationRepository
{
    private readonly ApplicationDbContext _db;

    public JobApplicationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(JobApplication application, CancellationToken ct = default)
    {
        _db.JobApplications.Add(application);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<JobApplication> applications, CancellationToken ct = default)
    {
        _db.JobApplications.AddRange(applications);
        await _db.SaveChangesAsync(ct);
    }

    public Task<JobApplication?> GetByIdAsync(int id, string userId, CancellationToken ct = default)
    {
        return _db.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    public Task<List<JobApplication>> GetRecentByUserAsync(string userId, int take = 200, CancellationToken ct = default)
    {
        return _db.JobApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<(List<JobApplication> Items, int TotalCount)> GetPagedByUserAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 15;

        var query = _db.JobApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ApplicationStatusCounts> GetStatusCountsAsync(string userId, CancellationToken ct = default)
    {
        var apps = _db.JobApplications.AsNoTracking().Where(a => a.UserId == userId);
        var total = await apps.CountAsync(ct);
        var sent = await apps.CountAsync(a => a.Status == JobApplicationStatus.Sent, ct);
        var failed = await apps.CountAsync(a => a.Status == JobApplicationStatus.Failed, ct);
        var queued = await apps.CountAsync(a => a.Status == JobApplicationStatus.Queued, ct);
        var interviewing = await apps.CountAsync(a => a.PipelineStatus == ApplicationPipelineStatus.Interviewing, ct);
        var offer = await apps.CountAsync(a => a.PipelineStatus == ApplicationPipelineStatus.Offer, ct);
        var rejected = await apps.CountAsync(a => a.PipelineStatus == ApplicationPipelineStatus.Rejected, ct);
        return new ApplicationStatusCounts(total, sent, failed, queued, interviewing, offer, rejected);
    }

    public Task<List<JobApplication>> GetPendingForDispatchAsync(int dispatchId, CancellationToken ct = default)
    {
        return _db.JobApplications
            .Where(a => a.BulkDispatchId == dispatchId && a.Status == JobApplicationStatus.Queued)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
    }

    public Task<List<JobApplication>> GetDueFollowUpsAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.JobApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.Status == JobApplicationStatus.Sent
                && a.NextFollowUpAt != null
                && a.NextFollowUpAt <= now)
            .OrderBy(a => a.NextFollowUpAt)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(JobApplication application, CancellationToken ct = default)
    {
        _db.JobApplications.Update(application);
        await _db.SaveChangesAsync(ct);
    }
}

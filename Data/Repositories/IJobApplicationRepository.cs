using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Data.Repositories;

public interface IJobApplicationRepository
{
    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<JobApplication> applications, CancellationToken ct = default);
    Task<JobApplication?> GetByIdAsync(int id, string userId, CancellationToken ct = default);
    Task<List<JobApplication>> GetRecentByUserAsync(string userId, int take = 200, CancellationToken ct = default);
    Task<(List<JobApplication> Items, int TotalCount)> GetPagedByUserAsync(string userId, int page, int pageSize, CancellationToken ct = default);
    Task<ApplicationStatusCounts> GetStatusCountsAsync(string userId, CancellationToken ct = default);
    Task<List<JobApplication>> GetPendingForDispatchAsync(int dispatchId, CancellationToken ct = default);
    Task<List<JobApplication>> GetDueFollowUpsAsync(string userId, CancellationToken ct = default);
    Task UpdateAsync(JobApplication application, CancellationToken ct = default);
}

public record ApplicationStatusCounts(
    int Total,
    int Sent,
    int Failed,
    int Queued,
    int Interviewing,
    int Offer,
    int Rejected);

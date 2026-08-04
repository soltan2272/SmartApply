using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Data.Repositories;

public interface IUserProfileRepository
{
    Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task UpsertAsync(UserProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Saves only the Bulk Apply email template fields for an existing profile.
    /// </summary>
    Task SaveBulkTemplateAsync(string userId, string subject, string body, CancellationToken ct = default);
}

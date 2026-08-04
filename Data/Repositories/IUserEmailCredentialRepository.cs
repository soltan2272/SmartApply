using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Data.Repositories;

public interface IUserEmailCredentialRepository
{
    Task<UserEmailCredential?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task UpsertAsync(UserEmailCredential credential, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);
}

using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class UserEmailCredentialRepository : IUserEmailCredentialRepository
{
    private readonly ApplicationDbContext _db;

    public UserEmailCredentialRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<UserEmailCredential?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return _db.UserEmailCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task UpsertAsync(UserEmailCredential credential, CancellationToken ct = default)
    {
        var existing = await _db.UserEmailCredentials.FirstOrDefaultAsync(c => c.UserId == credential.UserId, ct);
        if (existing == null)
        {
            credential.CreatedAt = DateTime.UtcNow;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.UserEmailCredentials.Add(credential);
        }
        else
        {
            existing.Provider = credential.Provider;
            existing.SenderEmail = credential.SenderEmail;
            existing.SenderName = credential.SenderName;
            existing.SmtpHost = credential.SmtpHost;
            existing.SmtpPort = credential.SmtpPort;
            existing.UseStartTls = credential.UseStartTls;

            if (credential.Secret != null)
            {
                existing.Secret = string.IsNullOrWhiteSpace(credential.Secret)
                    ? null
                    : credential.Secret;
            }

            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var existing = await _db.UserEmailCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing != null)
        {
            _db.UserEmailCredentials.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}

using JobApplicationBot.Data;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Services;

public interface IUserAccountSetupService
{
    /// <summary>
    /// Ensures a profile row exists and pre-fills contact email for new or returning users.
    /// Per-user Gmail credentials are saved only when the user submits them on Profile.
    /// </summary>
    Task EnsureProfileAsync(string userId, string email, CancellationToken ct = default);
}

public class UserAccountSetupService : IUserAccountSetupService
{
    private readonly ApplicationDbContext _db;

    public UserAccountSetupService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task EnsureProfileAsync(string userId, string email, CancellationToken ct = default)
    {
        var exists = await _db.UserProfiles.AsNoTracking().AnyAsync(p => p.UserId == userId, ct);
        if (!exists)
        {
            _db.UserProfiles.Add(new UserProfile
            {
                UserId = userId,
                ContactEmail = email,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Update ContactEmail only — avoid loading encrypted columns through the value converter
        // (old ciphertext from a previous key ring would otherwise risk being wiped on save).
        await _db.UserProfiles
            .Where(p => p.UserId == userId && (p.ContactEmail == null || p.ContactEmail == ""))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ContactEmail, email)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
    }
}

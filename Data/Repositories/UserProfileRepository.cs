using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class UserProfileRepository : IUserProfileRepository
{
    private readonly ApplicationDbContext _db;

    public UserProfileRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<UserProfile?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task UpsertAsync(UserProfile profile, CancellationToken ct = default)
    {
        var existing = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == profile.UserId, ct);
        if (existing == null)
        {
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;
            _db.UserProfiles.Add(profile);
        }
        else
        {
            existing.FullName = profile.FullName;
            existing.Title = profile.Title;
            existing.Phone = profile.Phone;
            existing.ContactEmail = profile.ContactEmail;
            existing.SkillsSummary = profile.SkillsSummary;
            existing.ExperienceSummary = profile.ExperienceSummary;
            // BulkTemplateSubject/Body are owned by SaveBulkTemplateAsync — do not clear them here.

            // PersonalGeminiApiKey is unused (BYOK removed) — do not overwrite from profile form.

            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveBulkTemplateAsync(string userId, string subject, string body, CancellationToken ct = default)
    {
        var existing = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Complete your profile before saving a bulk email template.");

        existing.BulkTemplateSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        existing.BulkTemplateBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

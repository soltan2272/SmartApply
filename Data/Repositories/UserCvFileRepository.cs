using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class UserCvFileRepository : IUserCvFileRepository
{
    private readonly ApplicationDbContext _db;

    public UserCvFileRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<UserCvFile?> GetAsync(string userId, CancellationToken ct = default)
    {
        return _db.UserCvFiles.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<CvFileMetadata?> GetMetadataAsync(string userId, CancellationToken ct = default)
    {
        return await _db.UserCvFiles
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new CvFileMetadata(c.FileName, c.ContentType, c.SizeBytes, c.UploadedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(UserCvFile cv, CancellationToken ct = default)
    {
        var existing = await _db.UserCvFiles.FirstOrDefaultAsync(c => c.UserId == cv.UserId, ct);
        if (existing == null)
        {
            cv.UploadedAt = DateTime.UtcNow;
            _db.UserCvFiles.Add(cv);
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Replace the row so the varbinary blob is always overwritten.
        // Assigning byte[] alone is unreliable with EF change tracking for large content.
        _db.UserCvFiles.Remove(existing);
        await _db.SaveChangesAsync(ct);

        cv.UploadedAt = DateTime.UtcNow;
        _db.UserCvFiles.Add(cv);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var existing = await _db.UserCvFiles.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing != null)
        {
            _db.UserCvFiles.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}

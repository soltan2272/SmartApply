using JobApplicationBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Data.Repositories;

public class AiUsageRepository : IAiUsageRepository
{
    private readonly ApplicationDbContext _db;

    public AiUsageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetCurrentMonthCountAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = await _db.AiUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Year == now.Year && u.Month == now.Month, ct);
        return row?.CallCount ?? 0;
    }

    public async Task<int?> TryIncrementIfBelowLimitAsync(string userId, int limit, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var year = now.Year;
        var month = now.Month;

        // Retry loop to recover from concurrent unique-key insertion races.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await _db.AiUsages
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Year == year && u.Month == month, ct);

            if (row == null)
            {
                if (limit <= 0) return null;
                row = new AiUsage
                {
                    UserId = userId,
                    Year = year,
                    Month = month,
                    CallCount = 1,
                    LastCallAt = now
                };
                _db.AiUsages.Add(row);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return 1;
                }
                catch (DbUpdateException)
                {
                    _db.Entry(row).State = EntityState.Detached;
                    continue;
                }
            }

            if (row.CallCount >= limit)
                return null;

            row.CallCount += 1;
            row.LastCallAt = now;

            try
            {
                await _db.SaveChangesAsync(ct);
                return row.CallCount;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(row).State = EntityState.Detached;
            }
        }

        return null;
    }

    public async Task ResetCurrentMonthAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = await _db.AiUsages
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Year == now.Year && u.Month == now.Month, ct);

        if (row == null)
        {
            _db.AiUsages.Add(new AiUsage
            {
                UserId = userId,
                Year = now.Year,
                Month = now.Month,
                CallCount = 0,
                LastCallAt = now
            });
        }
        else
        {
            row.CallCount = 0;
            row.LastCallAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}

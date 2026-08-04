using JobApplicationBot.Data;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Services.Billing;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Services.Admin;

public interface IAdminRequestService
{
    Task<SubscriptionRequest> RequestSubscriptionAsync(string userId, string plan, string? note, CancellationToken ct = default);
    Task<TokenResetRequest> RequestTokenResetAsync(string userId, string? note, CancellationToken ct = default);
    Task ApproveSubscriptionAsync(int requestId, string adminId, CancellationToken ct = default);
    Task RejectSubscriptionAsync(int requestId, string adminId, CancellationToken ct = default);
    Task ApproveTokenResetAsync(int requestId, string adminId, CancellationToken ct = default);
    Task RejectTokenResetAsync(int requestId, string adminId, CancellationToken ct = default);
    Task ResetUserTokensAsync(string userId, CancellationToken ct = default);
    Task SetUserPlanAsync(string userId, string plan, CancellationToken ct = default);
    Task<List<SubscriptionRequest>> GetPendingSubscriptionsAsync(CancellationToken ct = default);
    Task<List<TokenResetRequest>> GetPendingTokenResetsAsync(CancellationToken ct = default);
    Task<SubscriptionRequest?> GetUserPendingSubscriptionAsync(string userId, CancellationToken ct = default);
    Task<TokenResetRequest?> GetUserPendingTokenResetAsync(string userId, CancellationToken ct = default);
    Task<AdminDashboardStats> GetStatsAsync(CancellationToken ct = default);
    Task<List<AdminUserRow>> GetUsersAsync(CancellationToken ct = default);
}

public record AdminDashboardStats(
    int TotalUsers,
    int PendingSubscriptions,
    int PendingTokenResets,
    int LoggedInLast7Days);

public record AdminUserRow(
    string UserId,
    string Email,
    string Plan,
    int QuotaUsed,
    int QuotaLimit,
    DateTime? LastLoginAt,
    DateTime? CreatedAt,
    bool EmailConfirmed);

public class AdminRequestService : IAdminRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly ISubscriptionService _subscriptions;
    private readonly IAiQuotaService _quota;
    private readonly UserManager<ApplicationUser> _users;

    public AdminRequestService(
        ApplicationDbContext db,
        ISubscriptionService subscriptions,
        IAiQuotaService quota,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _subscriptions = subscriptions;
        _quota = quota;
        _users = users;
    }

    public async Task<SubscriptionRequest> RequestSubscriptionAsync(string userId, string plan, string? note, CancellationToken ct = default)
    {
        plan = SubscriptionPlans.Normalize(plan);
        if (plan == SubscriptionPlans.Free)
            throw new InvalidOperationException("Request Pro or Power — Free is already the default.");

        var existing = await _db.SubscriptionRequests
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == RequestStatuses.Pending, ct);
        if (existing != null)
            throw new InvalidOperationException("You already have a pending subscription request.");

        var request = new SubscriptionRequest
        {
            UserId = userId,
            RequestedPlan = plan,
            Status = RequestStatuses.Pending,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.SubscriptionRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<TokenResetRequest> RequestTokenResetAsync(string userId, string? note, CancellationToken ct = default)
    {
        var existing = await _db.TokenResetRequests
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == RequestStatuses.Pending, ct);
        if (existing != null)
            throw new InvalidOperationException("You already have a pending token reset request.");

        var request = new TokenResetRequest
        {
            UserId = userId,
            Status = RequestStatuses.Pending,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.TokenResetRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task ApproveSubscriptionAsync(int requestId, string adminId, CancellationToken ct = default)
    {
        var request = await _db.SubscriptionRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (request.Status != RequestStatuses.Pending)
            throw new InvalidOperationException("Request is not pending.");

        await _subscriptions.SetPlanAsync(request.UserId, request.RequestedPlan, ct: ct);
        request.Status = RequestStatuses.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RejectSubscriptionAsync(int requestId, string adminId, CancellationToken ct = default)
    {
        var request = await _db.SubscriptionRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (request.Status != RequestStatuses.Pending)
            throw new InvalidOperationException("Request is not pending.");

        request.Status = RequestStatuses.Rejected;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ApproveTokenResetAsync(int requestId, string adminId, CancellationToken ct = default)
    {
        var request = await _db.TokenResetRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (request.Status != RequestStatuses.Pending)
            throw new InvalidOperationException("Request is not pending.");

        await _quota.ResetTokensAsync(request.UserId, ct);
        request.Status = RequestStatuses.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RejectTokenResetAsync(int requestId, string adminId, CancellationToken ct = default)
    {
        var request = await _db.TokenResetRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Request not found.");
        if (request.Status != RequestStatuses.Pending)
            throw new InvalidOperationException("Request is not pending.");

        request.Status = RequestStatuses.Rejected;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminId;
        await _db.SaveChangesAsync(ct);
    }

    public Task ResetUserTokensAsync(string userId, CancellationToken ct = default)
        => _quota.ResetTokensAsync(userId, ct);

    public Task SetUserPlanAsync(string userId, string plan, CancellationToken ct = default)
        => _subscriptions.SetPlanAsync(userId, plan, ct: ct);

    public Task<List<SubscriptionRequest>> GetPendingSubscriptionsAsync(CancellationToken ct = default)
    {
        return _db.SubscriptionRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.Status == RequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<List<TokenResetRequest>> GetPendingTokenResetsAsync(CancellationToken ct = default)
    {
        return _db.TokenResetRequests
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.Status == RequestStatuses.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<SubscriptionRequest?> GetUserPendingSubscriptionAsync(string userId, CancellationToken ct = default)
    {
        return _db.SubscriptionRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == RequestStatuses.Pending, ct);
    }

    public Task<TokenResetRequest?> GetUserPendingTokenResetAsync(string userId, CancellationToken ct = default)
    {
        return _db.TokenResetRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == RequestStatuses.Pending, ct);
    }

    public async Task<AdminDashboardStats> GetStatsAsync(CancellationToken ct = default)
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var totalUsers = await _users.Users.CountAsync(ct);
        var pendingSubs = await _db.SubscriptionRequests.CountAsync(r => r.Status == RequestStatuses.Pending, ct);
        var pendingTokens = await _db.TokenResetRequests.CountAsync(r => r.Status == RequestStatuses.Pending, ct);
        var loggedIn = await _users.Users.CountAsync(u => u.LastLoginAt != null && u.LastLoginAt >= weekAgo, ct);
        return new AdminDashboardStats(totalUsers, pendingSubs, pendingTokens, loggedIn);
    }

    public async Task<List<AdminUserRow>> GetUsersAsync(CancellationToken ct = default)
    {
        var users = await _users.Users
            .AsNoTracking()
            .OrderByDescending(u => u.LastLoginAt)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);

        var rows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            var quota = await _quota.GetStatusAsync(user.Id, ct);
            rows.Add(new AdminUserRow(
                user.Id,
                user.Email ?? user.UserName ?? user.Id,
                SubscriptionPlans.Normalize(user.SubscriptionPlan),
                quota.Used,
                quota.Limit,
                user.LastLoginAt,
                null,
                user.EmailConfirmed));
        }

        return rows;
    }
}

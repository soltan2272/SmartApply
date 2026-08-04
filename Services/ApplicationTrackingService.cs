using JobApplicationBot.Data;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using JobApplicationBot.Models;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationBot.Services;

public interface IApplicationTrackingService
{
    Task RecordSingleSendAsync(string userId, EmailPreviewModel model, CancellationToken ct = default);
    Task<int> EnqueueBulkSendAsync(string userId, string subject, string body, List<string> recipients, CancellationToken ct = default);
    Task<BulkEmailDispatch?> GetDispatchAsync(string userId, int dispatchId, CancellationToken ct = default);
    Task<List<JobApplication>> GetApplicationsAsync(string userId, CancellationToken ct = default);
    Task<(List<JobApplication> Items, int TotalCount)> GetPagedApplicationsAsync(string userId, int page, int pageSize, CancellationToken ct = default);
    Task<ApplicationStatusCounts> GetStatusCountsAsync(string userId, CancellationToken ct = default);
    Task<List<JobApplication>> GetDueFollowUpsAsync(string userId, CancellationToken ct = default);
    Task<JobApplication?> GetApplicationAsync(string userId, int applicationId, CancellationToken ct = default);
    Task ScheduleFollowUpAsync(string userId, int applicationId, DateTime? nextFollowUpAtUtc, CancellationToken ct = default);
    Task SendFollowUpAsync(string userId, int applicationId, CancellationToken ct = default);
    Task UpdatePipelineStatusAsync(string userId, int applicationId, string pipelineStatus, CancellationToken ct = default);
}

public class ApplicationTrackingService : IApplicationTrackingService
{
    private readonly IJobApplicationRepository _applications;
    private readonly IBulkEmailDispatchRepository _dispatches;
    private readonly IUserProfileRepository _profiles;
    private readonly IUserCvFileRepository _cvFiles;
    private readonly IUserEmailCredentialRepository _credentials;
    private readonly IEmailSenderService _emailSender;
    private readonly ApplicationDbContext _db;

    public ApplicationTrackingService(
        IJobApplicationRepository applications,
        IBulkEmailDispatchRepository dispatches,
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository credentials,
        IEmailSenderService emailSender,
        ApplicationDbContext db)
    {
        _applications = applications;
        _dispatches = dispatches;
        _profiles = profiles;
        _cvFiles = cvFiles;
        _credentials = credentials;
        _emailSender = emailSender;
        _db = db;
    }

    public Task<List<JobApplication>> GetApplicationsAsync(string userId, CancellationToken ct = default)
        => _applications.GetRecentByUserAsync(userId, 500, ct);

    public Task<(List<JobApplication> Items, int TotalCount)> GetPagedApplicationsAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
        => _applications.GetPagedByUserAsync(userId, page, pageSize, ct);

    public Task<ApplicationStatusCounts> GetStatusCountsAsync(string userId, CancellationToken ct = default)
        => _applications.GetStatusCountsAsync(userId, ct);

    public Task<List<JobApplication>> GetDueFollowUpsAsync(string userId, CancellationToken ct = default)
        => _applications.GetDueFollowUpsAsync(userId, ct);

    public Task<JobApplication?> GetApplicationAsync(string userId, int applicationId, CancellationToken ct = default)
        => _applications.GetByIdAsync(applicationId, userId, ct);

    public Task<BulkEmailDispatch?> GetDispatchAsync(string userId, int dispatchId, CancellationToken ct = default)
        => _dispatches.GetByIdAsync(dispatchId, userId, ct);

    public async Task RecordSingleSendAsync(string userId, EmailPreviewModel model, CancellationToken ct = default)
    {
        var entry = new JobApplication
        {
            UserId = userId,
            JobTitle = model.JobTitle,
            CompanyName = model.CompanyName,
            RecipientEmail = model.ToEmail,
            Subject = model.Subject,
            Body = model.Body,
            Status = JobApplicationStatus.Sent,
            PipelineStatus = ApplicationPipelineStatus.Applied,
            MatchScore = model.MatchScore > 0 ? model.MatchScore : null,
            Source = JobApplicationSource.Single,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow
        };

        await _applications.AddAsync(entry, ct);
    }

    public async Task<int> EnqueueBulkSendAsync(string userId, string subject, string body, List<string> recipients, CancellationToken ct = default)
    {
        // Commit dispatch + recipient rows in one transaction so the background worker
        // cannot pick up a Queued dispatch before the JobApplications rows exist.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var dispatch = new BulkEmailDispatch
            {
                UserId = userId,
                Subject = subject,
                Body = body,
                Status = BulkEmailDispatchStatus.Queued,
                TotalRecipients = recipients.Count,
                CreatedAt = DateTime.UtcNow
            };
            _db.BulkEmailDispatches.Add(dispatch);
            await _db.SaveChangesAsync(ct);

            var profile = await _profiles.GetByUserIdAsync(userId, ct);
            var jobTitle = profile?.Title ?? string.Empty;

            var apps = recipients.Select(email => new JobApplication
            {
                UserId = userId,
                JobTitle = jobTitle,
                CompanyName = "Bulk outreach",
                RecipientEmail = email,
                Subject = subject,
                Body = body,
                Status = JobApplicationStatus.Queued,
                PipelineStatus = ApplicationPipelineStatus.Applied,
                Source = JobApplicationSource.Bulk,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                BulkDispatchId = dispatch.Id
            }).ToList();

            _db.JobApplications.AddRange(apps);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return dispatch.Id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ScheduleFollowUpAsync(string userId, int applicationId, DateTime? nextFollowUpAtUtc, CancellationToken ct = default)
    {
        var app = await _db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (app == null)
            throw new InvalidOperationException("Application was not found.");

        app.NextFollowUpAt = nextFollowUpAtUtc;
        app.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SendFollowUpAsync(string userId, int applicationId, CancellationToken ct = default)
    {
        var app = await _db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (app == null)
            throw new InvalidOperationException("Application was not found.");
        if (app.Status != JobApplicationStatus.Sent)
            throw new InvalidOperationException("Follow-up can only be sent for successful applications.");

        var profile = await _profiles.GetByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException("Complete your profile before sending follow-ups.");
        var credential = await _credentials.GetByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException("Save your Gmail App Password before sending follow-ups.");
        if (string.IsNullOrWhiteSpace(credential.Secret))
            throw new InvalidOperationException("Save your Gmail App Password before sending follow-ups.");

        var followUpSubject = $"Follow-up: {app.Subject}";
        var followUpBody = $"""
            Dear Hiring Team,

            I hope you are doing well.

            I wanted to kindly follow up on my previous application regarding {app.JobTitle}. I remain very interested in this opportunity and would be grateful for any update on the process.

            Thank you for your time and consideration.

            Best regards,
            {profile.FullName}
            """;

        var sender = new EmailSenderAccount(
            credential.SenderEmail,
            string.IsNullOrWhiteSpace(credential.SenderName) ? profile.FullName : credential.SenderName,
            credential.Secret,
            credential.SmtpHost,
            credential.SmtpPort,
            credential.UseStartTls);

        await _emailSender.SendEmailAsync(sender, app.RecipientEmail, followUpSubject, followUpBody);

        app.LastFollowUpSentAt = DateTime.UtcNow;
        app.NextFollowUpAt = null;
        app.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var followUpRecord = new JobApplication
        {
            UserId = userId,
            JobTitle = app.JobTitle,
            CompanyName = app.CompanyName,
            RecipientEmail = app.RecipientEmail,
            Subject = followUpSubject,
            Body = followUpBody,
            Status = JobApplicationStatus.Sent,
            PipelineStatus = app.PipelineStatus,
            Source = JobApplicationSource.FollowUp,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SentAt = DateTime.UtcNow
        };
        _db.JobApplications.Add(followUpRecord);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdatePipelineStatusAsync(string userId, int applicationId, string pipelineStatus, CancellationToken ct = default)
    {
        if (!ApplicationPipelineStatus.IsValid(pipelineStatus))
            throw new InvalidOperationException("Invalid pipeline status.");

        var app = await _db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (app == null)
            throw new InvalidOperationException("Application was not found.");

        app.PipelineStatus = ApplicationPipelineStatus.All
            .First(s => s.Equals(pipelineStatus, StringComparison.OrdinalIgnoreCase));
        app.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

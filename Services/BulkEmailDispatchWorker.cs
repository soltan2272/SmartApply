using JobApplicationBot.Data.Entities;
using JobApplicationBot.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobApplicationBot.Services;

public class BulkEmailDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulkEmailDispatchWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public BulkEmailDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<BulkEmailDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bulk email dispatch worker started as {WorkerId}.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedDispatchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk email dispatch worker iteration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessQueuedDispatchesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatches = scope.ServiceProvider.GetRequiredService<IBulkEmailDispatchRepository>();
        var applications = scope.ServiceProvider.GetRequiredService<IJobApplicationRepository>();
        var profiles = scope.ServiceProvider.GetRequiredService<IUserProfileRepository>();
        var cvFiles = scope.ServiceProvider.GetRequiredService<IUserCvFileRepository>();
        var credentials = scope.ServiceProvider.GetRequiredService<IUserEmailCredentialRepository>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSenderService>();

        var requeued = await dispatches.RequeueStuckDispatchesAsync(ct);
        if (requeued > 0)
        {
            _logger.LogWarning("Re-queued {Count} stuck bulk dispatch(es) that still had pending recipients.", requeued);
        }

        // Claim up to 5 dispatches via leasing (safe across multiple app instances).
        for (var i = 0; i < 5; i++)
        {
            var dispatch = await dispatches.TryClaimNextAsync(_workerId, LeaseDuration, ct);
            if (dispatch == null)
                break;

            await ProcessOneDispatchAsync(dispatch, dispatches, applications, profiles, cvFiles, credentials, emailSender, ct);
        }
    }

    private async Task ProcessOneDispatchAsync(
        BulkEmailDispatch dispatch,
        IBulkEmailDispatchRepository dispatches,
        IJobApplicationRepository applications,
        IUserProfileRepository profiles,
        IUserCvFileRepository cvFiles,
        IUserEmailCredentialRepository credentials,
        IEmailSenderService emailSender,
        CancellationToken ct)
    {
        var profile = await profiles.GetByUserIdAsync(dispatch.UserId, ct);
        var credential = await credentials.GetByUserIdAsync(dispatch.UserId, ct);
        var cv = await cvFiles.GetAsync(dispatch.UserId, ct);

        if (profile == null || credential == null || string.IsNullOrWhiteSpace(credential.Secret) || cv == null || cv.Content.Length == 0)
        {
            var pendingToFail = await applications.GetPendingForDispatchAsync(dispatch.Id, ct);
            foreach (var app in pendingToFail)
            {
                app.Status = JobApplicationStatus.Failed;
                app.ErrorMessage = "Missing profile, Gmail App Password, or CV required to send bulk emails.";
                app.UpdatedAt = DateTime.UtcNow;
                await applications.UpdateAsync(app, ct);
                dispatch.FailedCount += 1;
            }

            ClearLeaseAndComplete(dispatch);
            await dispatches.UpdateAsync(dispatch, ct);
            _logger.LogWarning("Dispatch {DispatchId} completed with failures: missing profile/credential/CV.", dispatch.Id);
            return;
        }

        var sender = new EmailSenderAccount(
            credential.SenderEmail,
            string.IsNullOrWhiteSpace(credential.SenderName) ? profile.FullName : credential.SenderName,
            credential.Secret!,
            credential.SmtpHost,
            credential.SmtpPort,
            credential.UseStartTls);
        var attachment = new EmailAttachment(cv.FileName, cv.ContentType, cv.Content);

        var pending = await applications.GetPendingForDispatchAsync(dispatch.Id, ct);
        if (pending.Count == 0)
        {
            if (dispatch.SentCount + dispatch.FailedCount < dispatch.TotalRecipients)
            {
                dispatch.Status = BulkEmailDispatchStatus.Queued;
                dispatch.LeaseOwner = null;
                dispatch.LeaseExpiresAt = null;
                await dispatches.UpdateAsync(dispatch, ct);
                _logger.LogWarning(
                    "Dispatch {DispatchId} had no pending recipients yet ({Sent}/{Total}); re-queued.",
                    dispatch.Id, dispatch.SentCount + dispatch.FailedCount, dispatch.TotalRecipients);
                return;
            }

            ClearLeaseAndComplete(dispatch);
            await dispatches.UpdateAsync(dispatch, ct);
            return;
        }

        _logger.LogInformation(
            "Processing dispatch {DispatchId} as {WorkerId}: {Pending} pending of {Total} recipients.",
            dispatch.Id, _workerId, pending.Count, dispatch.TotalRecipients);

        var processed = 0;
        foreach (var app in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await emailSender.SendEmailAsync(sender, app.RecipientEmail, dispatch.Subject, dispatch.Body, attachment);
                app.Status = JobApplicationStatus.Sent;
                app.PipelineStatus = ApplicationPipelineStatus.Applied;
                app.SentAt = DateTime.UtcNow;
                app.ErrorMessage = null;
                dispatch.SentCount += 1;
            }
            catch (Exception ex)
            {
                app.Status = JobApplicationStatus.Failed;
                app.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                dispatch.FailedCount += 1;
                _logger.LogWarning(ex, "Failed to send bulk email to {Email} for dispatch {DispatchId}.", app.RecipientEmail, dispatch.Id);
            }

            app.UpdatedAt = DateTime.UtcNow;
            await applications.UpdateAsync(app, ct);
            await dispatches.UpdateAsync(dispatch, ct);

            processed++;
            if (processed % 10 == 0)
                await dispatches.RenewLeaseAsync(dispatch.Id, _workerId, LeaseDuration, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
        }

        ClearLeaseAndComplete(dispatch);
        await dispatches.UpdateAsync(dispatch, ct);
        _logger.LogInformation(
            "Dispatch {DispatchId} completed. Sent={Sent}, Failed={Failed}.",
            dispatch.Id, dispatch.SentCount, dispatch.FailedCount);
    }

    private static void ClearLeaseAndComplete(BulkEmailDispatch dispatch)
    {
        dispatch.Status = BulkEmailDispatchStatus.Completed;
        dispatch.CompletedAt = DateTime.UtcNow;
        dispatch.LeaseOwner = null;
        dispatch.LeaseExpiresAt = null;
    }
}

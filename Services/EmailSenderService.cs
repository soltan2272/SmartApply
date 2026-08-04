using JobApplicationBot.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JobApplicationBot.Services;

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

public record EmailSenderAccount(
    string SenderEmail,
    string SenderName,
    string Secret,
    string SmtpHost,
    int SmtpPort,
    bool UseStartTls);

public interface IEmailSenderService
{
    Task SendEmailAsync(string toEmail, string subject, string body, EmailAttachment? attachment = null);

    Task SendEmailAsync(EmailSenderAccount account, string toEmail, string subject, string body, EmailAttachment? attachment = null);

    /// <summary>
    /// Sends an email whose body is HTML (used by ASP.NET Core Identity for password-reset / confirmation links).
    /// </summary>
    Task SendHtmlEmailAsync(string toEmail, string subject, string htmlBody);
}

public class EmailSenderService : IEmailSenderService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailSenderService> _logger;

    public EmailSenderService(IOptions<EmailSettings> settings, ILogger<EmailSenderService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body, EmailAttachment? attachment = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail) || string.IsNullOrWhiteSpace(_settings.SenderPassword))
        {
            throw new InvalidOperationException(
                "SMTP credentials are not configured. Set Email:SenderEmail and Email:SenderPassword (use a Gmail App Password, not your normal password).");
        }

        var account = new EmailSenderAccount(
            _settings.SenderEmail,
            _settings.SenderName,
            _settings.SenderPassword,
            _settings.SmtpServer,
            _settings.SmtpPort,
            UseStartTls: true);

        return SendInternalAsync(account, toEmail, subject, textBody: body, htmlBody: null, attachment);
    }

    public Task SendEmailAsync(EmailSenderAccount account, string toEmail, string subject, string body, EmailAttachment? attachment = null)
        => SendInternalAsync(account, toEmail, subject, textBody: body, htmlBody: null, attachment);

    public Task SendHtmlEmailAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail) || string.IsNullOrWhiteSpace(_settings.SenderPassword))
        {
            throw new InvalidOperationException(
                "SMTP credentials are not configured. Set Email:SenderEmail and Email:SenderPassword (use a Gmail App Password, not your normal password).");
        }

        var account = new EmailSenderAccount(
            _settings.SenderEmail,
            _settings.SenderName,
            _settings.SenderPassword,
            _settings.SmtpServer,
            _settings.SmtpPort,
            UseStartTls: true);

        return SendInternalAsync(account, toEmail, subject, textBody: null, htmlBody: htmlBody, attachment: null);
    }

    private async Task SendInternalAsync(EmailSenderAccount account, string toEmail, string subject, string? textBody, string? htmlBody, EmailAttachment? attachment)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.SenderName, account.SenderEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder();
        if (!string.IsNullOrEmpty(htmlBody)) builder.HtmlBody = htmlBody;
        if (!string.IsNullOrEmpty(textBody)) builder.TextBody = textBody;

        if (attachment != null && attachment.Content.Length > 0)
        {
            var contentType = MimeKit.ContentType.TryParse(attachment.ContentType, out var parsed)
                ? parsed
                : new MimeKit.ContentType("application", "octet-stream");
            builder.Attachments.Add(attachment.FileName, attachment.Content, contentType);
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = account.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions);
        await client.AuthenticateAsync(account.SenderEmail, account.Secret);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation(
            "Email sent from {From} to {To} with subject {Subject}.",
            account.SenderEmail,
            toEmail,
            subject);
    }
}

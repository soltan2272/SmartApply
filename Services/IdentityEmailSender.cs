using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using Microsoft.AspNetCore.Identity;

namespace JobApplicationBot.Services;

/// <summary>
/// Sends ASP.NET Core Identity emails (confirmation, password reset) via global SMTP settings.
/// </summary>
public class IdentityEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailSenderService _emailSender;

    public IdentityEmailSender(IEmailSenderService emailSender)
    {
        _emailSender = emailSender;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        => _emailSender.SendHtmlEmailAsync(
            email,
            $"Confirm your {AppBranding.Name} account",
            $"<p>Please confirm your {AppBranding.Name} account by <a href='{confirmationLink}'>clicking here</a>.</p>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        => _emailSender.SendHtmlEmailAsync(
            email,
            $"Reset your {AppBranding.Name} password",
            $"<p>Please reset your {AppBranding.Name} password by <a href='{resetLink}'>clicking here</a>.</p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        => _emailSender.SendHtmlEmailAsync(
            email,
            $"Reset your {AppBranding.Name} password",
            $"<p>Your {AppBranding.Name} password reset code is: <strong>{resetCode}</strong></p>");
}

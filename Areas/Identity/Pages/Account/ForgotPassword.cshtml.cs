using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using JobApplicationBot.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace JobApplicationBot.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender<ApplicationUser> _emailSender;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        IEmailSender<ApplicationUser> emailSender,
        ILogger<ForgotPasswordModel> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            _logger.LogInformation("Forgot password requested for unknown email {Email}.", Input.Email);
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            _logger.LogWarning(
                "Sending password reset to {Email} although email is not confirmed (UserId={UserId}).",
                Input.Email,
                user.Id);
        }

        var code = await _userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { area = "Identity", code, email = Input.Email },
            protocol: Request.Scheme)!;

        try
        {
            await _emailSender.SendPasswordResetLinkAsync(
                user,
                Input.Email,
                HtmlEncoder.Default.Encode(callbackUrl));

            _logger.LogInformation("Password reset email sent to {Email}.", Input.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}.", Input.Email);
            ModelState.AddModelError(string.Empty,
                "We could not send the reset email. Check that Email:SenderPassword is configured (Gmail App Password) and try again.");
            return Page();
        }

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobApplicationBot.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    public string Email { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }

    public void OnGet(string email, string? returnUrl = null)
    {
        Email = email;
        ReturnUrl = returnUrl;
    }
}

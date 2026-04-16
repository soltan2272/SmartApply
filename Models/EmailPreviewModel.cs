using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Models;

public class EmailPreviewModel
{
    [Required(ErrorMessage = "Recipient email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [Display(Name = "To")]
    public string ToEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Subject is required")]
    [Display(Name = "Subject")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email body is required")]
    [Display(Name = "Email Body")]
    public string Body { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<string> MatchedSkills { get; set; } = [];
    public string CvPath { get; set; } = string.Empty;
}

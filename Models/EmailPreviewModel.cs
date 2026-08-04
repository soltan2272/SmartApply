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

    /// <summary>0–100 skills fit score vs the job description.</summary>
    [Range(0, 100)]
    public int MatchScore { get; set; }

    /// <summary>
    /// Set by the controller (not bound from the form) to indicate whether the current
    /// user has a CV stored in the database; used by the Preview view for display only.
    /// </summary>
    public bool HasCvOnFile { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace JobApplicationBot.Models;

public class ProfileViewModel
{
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(200)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Contact Email (shown in application emails)")]
    public string ContactEmail { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Skills Summary")]
    public string SkillsSummary { get; set; } = string.Empty;

    [StringLength(4000)]
    [Display(Name = "Experience Summary")]
    public string ExperienceSummary { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Sender Gmail address")]
    public string SenderEmail { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Sender display name")]
    public string SenderName { get; set; } = string.Empty;

    [StringLength(2000)]
    [DataType(DataType.Password)]
    [Display(Name = "Gmail App Password")]
    public string? SenderAppPassword { get; set; }

    public bool HasEmailCredential { get; set; }

    [Display(Name = "Upload CV (PDF, DOCX)")]
    public IFormFile? CvUpload { get; set; }

    public string? ExistingCvFileName { get; set; }
    public long? ExistingCvSizeBytes { get; set; }
    public DateTime? ExistingCvUploadedAt { get; set; }

    public int QuotaUsed { get; set; }
    public int QuotaLimit { get; set; }
    public int QuotaRemaining => Math.Max(0, QuotaLimit - QuotaUsed);
    public string SubscriptionPlan { get; set; } = "Free";
}

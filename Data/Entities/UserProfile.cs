using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobApplicationBot.Data.Entities;

public class UserProfile
{
    [Key]
    [ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string SkillsSummary { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string ExperienceSummary { get; set; } = string.Empty;

    /// <summary>
    /// Saved Bulk Apply email subject. When set, pre-fills the Bulk Apply form.
    /// </summary>
    [MaxLength(300)]
    public string? BulkTemplateSubject { get; set; }

    /// <summary>
    /// Saved Bulk Apply email body. When set, pre-fills the Bulk Apply form.
    /// </summary>
    [MaxLength(5000)]
    public string? BulkTemplateBody { get; set; }

    /// <summary>
    /// Optional per-user Gemini API key. When set, overrides the shared key and bypasses the freemium quota.
    /// Stored encrypted via a value converter backed by IDataProtector.
    /// </summary>
    [MaxLength(2000)]
    public string? PersonalGeminiApiKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

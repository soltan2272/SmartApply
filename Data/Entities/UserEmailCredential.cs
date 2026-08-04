using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobApplicationBot.Data.Entities;

public class UserEmailCredential
{
    [Key]
    [ForeignKey(nameof(User))]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = "GmailSmtp";

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string SenderEmail { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SenderName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Provider secret for the selected sending method. For GmailSmtp this is the
    /// Gmail App Password, stored encrypted by ApplicationDbContext.
    /// </summary>
    [MaxLength(2000)]
    public string? Secret { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

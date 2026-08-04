using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobApplicationBot.Data.Entities;

public static class BulkEmailDispatchStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
}

public class BulkEmailDispatch
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = BulkEmailDispatchStatus.Queued;

    public int TotalRecipients { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>Worker instance that currently holds the lease (multi-instance safe).</summary>
    [MaxLength(100)]
    public string? LeaseOwner { get; set; }

    /// <summary>UTC time when the processing lease expires and another worker may reclaim.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<JobApplication> Applications { get; set; } = [];
}

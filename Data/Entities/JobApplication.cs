using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobApplicationBot.Data.Entities;

public static class JobApplicationStatus
{
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

public static class JobApplicationSource
{
    public const string Single = "Single";
    public const string Bulk = "Bulk";
    public const string FollowUp = "FollowUp";
}

/// <summary>CRM pipeline after delivery (separate from Queued/Sent/Failed).</summary>
public static class ApplicationPipelineStatus
{
    public const string Applied = "Applied";
    public const string Interviewing = "Interviewing";
    public const string Offer = "Offer";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";
    public const string NoResponse = "NoResponse";

    public static readonly string[] All =
    [
        Applied, Interviewing, Offer, Rejected, Withdrawn, NoResponse
    ];

    public static bool IsValid(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

public class JobApplication
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [MaxLength(200)]
    public string JobTitle { get; set; } = string.Empty;

    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string RecipientEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = JobApplicationStatus.Queued;

    /// <summary>CRM stage: Applied, Interviewing, Offer, Rejected, Withdrawn, NoResponse.</summary>
    [MaxLength(20)]
    public string PipelineStatus { get; set; } = ApplicationPipelineStatus.Applied;

    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = JobApplicationSource.Single;

    /// <summary>0–100 skills match score when known (single AI apply).</summary>
    public int? MatchScore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? LastFollowUpSentAt { get; set; }
    public DateTime? NextFollowUpAt { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public int? BulkDispatchId { get; set; }
    public BulkEmailDispatch? BulkDispatch { get; set; }
}

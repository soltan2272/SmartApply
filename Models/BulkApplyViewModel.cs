using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Models;

public class BulkApplyViewModel
{
    public const int MaxRecipients = 500;

    public List<string> RecipientEmails { get; set; } = [string.Empty];

    /// <summary>
    /// Free-form text (job description, notes, pasted list). Emails are extracted from this text
    /// and merged with <see cref="RecipientEmails"/> before sending.
    /// </summary>
    [Display(Name = "Paste description with emails")]
    [StringLength(20000)]
    public string? SourceText { get; set; }

    public int ExtractedEmailCount { get; set; }

    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(300)]
    [Display(Name = "Subject")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email body is required.")]
    [StringLength(5000)]
    [Display(Name = "Email body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>True when the form subject/body came from a previously saved template.</summary>
    public bool HasSavedTemplate { get; set; }

    public string DefaultSubjectPreview { get; set; } = string.Empty;
    public string DefaultBodyPreview { get; set; } = string.Empty;

    public bool HasCvOnFile { get; set; }
    public string? CvFileName { get; set; }
    public bool HasEmailCredential { get; set; }
    public bool HasCompleteProfile { get; set; }

    public int? LastDispatchId { get; set; }
    public string? LastDispatchStatus { get; set; }
    public int LastDispatchTotalRecipients { get; set; }
    public int LastDispatchSentCount { get; set; }
    public int LastDispatchFailedCount { get; set; }

    public List<BulkApplySendResult> Results { get; set; } = [];
}

public class BulkApplySendResult
{
    public string Email { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

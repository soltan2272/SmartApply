namespace SmartApplyHub.Shared.Models;

public class ExtractEmailsRequest
{
    public string SourceText { get; set; } = string.Empty;
}

public class ExtractEmailsResponse
{
    public List<string> Emails { get; set; } = [];
    public int Count { get; set; }
}

public class BulkTemplateDto
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool HasCvOnFile { get; set; }
    public string? CvFileName { get; set; }
    public bool HasEmailCredential { get; set; }
    public bool HasCompleteProfile { get; set; }
    public int BulkMaxRecipients { get; set; }
    public string Plan { get; set; } = string.Empty;
}

public class SaveBulkTemplateRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class BulkSendRequest
{
    public string SourceText { get; set; } = string.Empty;
    public List<string> RecipientEmails { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class BulkSendResponse
{
    public int DispatchId { get; set; }
    public int RecipientCount { get; set; }
}

public class BulkDispatchDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalRecipients { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PendingRequestResponse
{
    public bool HasPending { get; set; }
    public RequestStatusDto? Request { get; set; }
}

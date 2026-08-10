using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Models.Api;

public class ApplicationDto
{
    public int Id { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PipelineStatus { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int? MatchScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? NextFollowUpAt { get; set; }
}

public class ScheduleFollowUpRequest
{
    public DateTime? NextFollowUpAt { get; set; }
}

public class UpdatePipelineRequest
{
    [Required]
    public string PipelineStatus { get; set; } = string.Empty;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int PageSize { get; set; }
}

public class ApplicationStatsDto
{
    public int TotalApplications { get; set; }
    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
    public int TotalQueued { get; set; }
    public int PipelineInterviewing { get; set; }
    public int PipelineOffer { get; set; }
    public int PipelineRejected { get; set; }
}

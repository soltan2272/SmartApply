namespace SmartApplyHub.Shared.Models;

public class JobSearchRequest
{
    public string Title { get; set; } = string.Empty;
    public string? ExperienceLevel { get; set; }
    public string? DatePosted { get; set; }
    public string? Location { get; set; }
    public bool RankWithAi { get; set; }
}

public class JobSearchResultDto
{
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DatePosted { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Snippet { get; set; }
    public string ResultType { get; set; } = "Job";
    public string DisplayTag =>
        string.Equals(ResultType, "Post", StringComparison.OrdinalIgnoreCase)
            ? "Hiring post"
            : "Job alert";
    public int? MatchScore { get; set; }
    public string? MatchReason { get; set; }
}

public class JobSearchResponse
{
    public List<JobSearchResultDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public bool RankedWithAi { get; set; }
    public int JobAlertCount { get; set; }
    public int HiringPostCount { get; set; }
    public string? HiringPostsStatus { get; set; }
    public string? AiRankSkipReason { get; set; }
}

public class JobAnalyzeRequest
{
    public string? JobUrl { get; set; }
    public string? JobDescription { get; set; }
}

public class EmailPreviewDto
{
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<string> MatchedSkills { get; set; } = [];
    public int MatchScore { get; set; }
    public bool HasCvOnFile { get; set; }
}

public class SendEmailRequest
{
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<string> MatchedSkills { get; set; } = [];
    public int MatchScore { get; set; }
}

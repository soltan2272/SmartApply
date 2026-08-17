namespace JobApplicationBot.Models;

public class JobSearchResultItem
{
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DatePosted { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Snippet { get; set; }

    /// <summary>"Job" for LinkedIn job alerts/listings; "Post" for hiring posts.</summary>
    public string ResultType { get; set; } = "Job";

    public int? MatchScore { get; set; }
    public string? MatchReason { get; set; }

    public string DisplayTag =>
        string.Equals(ResultType, "Post", StringComparison.OrdinalIgnoreCase)
            ? "Hiring post"
            : "Job alert";
}

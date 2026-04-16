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
    public string ResultType { get; set; } = "Job";
}

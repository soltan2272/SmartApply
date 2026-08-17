namespace JobApplicationBot.Models;

public class JobSearchViewModel
{
    public string? SearchId { get; set; }
    public JobSearchFilter Filter { get; set; } = new();
    public List<JobSearchResultItem> Results { get; set; } = [];
    public bool HasSearched { get; set; }
    public bool RankedWithAi { get; set; }
    /// <summary>Why Rank with AI did not run (key / profile / quota).</summary>
    public string? AiRankSkipReason { get; set; }
    /// <summary>Hiring-post discovery status when zero or provider failed.</summary>
    public string? HiringPostsStatus { get; set; }
    /// <summary>Server has a shared Ai:ApiKey (personal key may still work).</summary>
    public bool AiConfiguredOnServer { get; set; } = true;

    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 15;
    public int TotalCount => Results.Count;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public int JobAlertCount => Results.Count(r => !string.Equals(r.ResultType, "Post", StringComparison.OrdinalIgnoreCase));
    public int HiringPostCount => Results.Count(r => string.Equals(r.ResultType, "Post", StringComparison.OrdinalIgnoreCase));

    public List<JobSearchResultItem> PagedResults =>
        Results.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
}

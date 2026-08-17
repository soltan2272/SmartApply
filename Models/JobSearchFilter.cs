namespace JobApplicationBot.Models;

public class JobSearchFilter
{
    public string? Title { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? DatePosted { get; set; }

    /// <summary>Search location; empty means all countries.</summary>
    public string? Location { get; set; }

    /// <summary>When true, consume AI quota and rank results against the user profile.</summary>
    public bool RankWithAi { get; set; }
}

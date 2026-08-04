namespace JobApplicationBot.Models;

public class JobSearchFilter
{
    public string? Title { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? DatePosted { get; set; }

    /// <summary>Search location; defaults to Egypt.</summary>
    public string? Location { get; set; } = "Egypt";
}

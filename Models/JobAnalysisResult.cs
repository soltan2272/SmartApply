namespace JobApplicationBot.Models;

public class JobAnalysisResult
{
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = [];
    public List<string> Responsibilities { get; set; } = [];
    public string ExperienceLevel { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;

    /// <summary>0–100 fit score vs candidate profile/CV (filled during email generation).</summary>
    public int MatchScore { get; set; }
}

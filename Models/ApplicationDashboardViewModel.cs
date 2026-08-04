using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Models;

public class ApplicationDashboardViewModel
{
    public const int PageSize = 15;

    public List<JobApplication> Applications { get; set; } = [];
    public List<JobApplication> DueFollowUps { get; set; } = [];

    public int CurrentPage { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public int TotalApplications { get; set; }
    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
    public int TotalQueued { get; set; }
    public int PipelineInterviewing { get; set; }
    public int PipelineOffer { get; set; }
    public int PipelineRejected { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace JobApplicationBot.Models;

public class JobInput
{
    [Display(Name = "Job URL")]
    public string? JobUrl { get; set; }

    [Display(Name = "Job Description")]
    public string? JobDescription { get; set; }
}

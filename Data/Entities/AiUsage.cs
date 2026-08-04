using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobApplicationBot.Data.Entities;

public class AiUsage
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public int CallCount { get; set; }

    public DateTime LastCallAt { get; set; } = DateTime.UtcNow;
}

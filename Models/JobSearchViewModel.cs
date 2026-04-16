namespace JobApplicationBot.Models;

public class JobSearchViewModel
{
    public string? SearchId { get; set; }
    public JobSearchFilter Filter { get; set; } = new();
    public List<JobSearchResultItem> Results { get; set; } = [];
    public bool HasSearched { get; set; }

    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount => Results.Count;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<JobSearchResultItem> PagedResults =>
        Results.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
}

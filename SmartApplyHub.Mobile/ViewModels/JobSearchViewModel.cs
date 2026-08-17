using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class JobSearchViewModel : ObservableObject
{
    private const int PageSize = 15;
    private readonly ApiService _api;
    private List<JobSearchResultDto> _allResults = [];

    public JobSearchViewModel(ApiService api)
    {
        _api = api;
        ExperienceLevels =
        [
            new FilterOption("", "Any level"),
            new FilterOption("1", "Internship"),
            new FilterOption("2", "Entry (0-2 yrs)"),
            new FilterOption("3", "Associate (2-5 yrs)"),
            new FilterOption("4", "Mid-Senior (5-10 yrs)"),
            new FilterOption("5", "Director (10+ yrs)"),
            new FilterOption("6", "Executive")
        ];
        DatePostedOptions =
        [
            new FilterOption("", "Any time"),
            new FilterOption("r86400", "Today"),
            new FilterOption("r604800", "Past week"),
            new FilterOption("r2592000", "Past month")
        ];
        SelectedExperience = ExperienceLevels[0];
        SelectedDatePosted = DatePostedOptions[0];
    }

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string location = string.Empty;
    [ObservableProperty] private FilterOption? selectedExperience;
    [ObservableProperty] private FilterOption? selectedDatePosted;
    [ObservableProperty] private bool rankWithAi;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool hasSearched;
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int totalPages = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private string resultsSummary = string.Empty;
    [ObservableProperty] private bool rankedWithAi;
    [ObservableProperty] private string? hiringPostsStatus;
    [ObservableProperty] private string? aiRankSkipReason;

    public List<FilterOption> ExperienceLevels { get; }
    public List<FilterOption> DatePostedOptions { get; }
    public ObservableCollection<JobSearchResultDto> Results { get; } = [];

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Please enter a job title.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            HasSearched = true;
            RankedWithAi = false;
            HiringPostsStatus = null;
            AiRankSkipReason = null;
            Results.Clear();

            var response = await _api.SearchJobsAsync(new JobSearchRequest
            {
                Title = Title.Trim(),
                Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
                ExperienceLevel = string.IsNullOrWhiteSpace(SelectedExperience?.Value) ? null : SelectedExperience!.Value,
                DatePosted = string.IsNullOrWhiteSpace(SelectedDatePosted?.Value) ? null : SelectedDatePosted!.Value,
                RankWithAi = RankWithAi
            });

            _allResults = response.Items ?? [];
            TotalCount = response.TotalCount > 0 ? response.TotalCount : _allResults.Count;
            RankedWithAi = response.RankedWithAi;
            HiringPostsStatus = response.HiringPostsStatus;
            AiRankSkipReason = response.AiRankSkipReason;
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
            CurrentPage = 1;
            ApplyPage();

            if (!string.IsNullOrWhiteSpace(AiRankSkipReason))
                ErrorMessage = AiRankSkipReason;
            else if (!string.IsNullOrWhiteSpace(HiringPostsStatus) && response.HiringPostCount == 0)
                ErrorMessage = HiringPostsStatus;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            ApplyPage();
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            ApplyPage();
        }
    }

    [RelayCommand]
    private async Task SelectJobAsync(JobSearchResultDto? job)
    {
        if (job == null) return;
        var navParams = new Dictionary<string, object> { { "Job", job } };
        await Shell.Current.GoToAsync("JobAnalyzeDetail", navParams);
    }

    private void ApplyPage()
    {
        Results.Clear();
        foreach (var item in _allResults.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Results.Add(item);

        CanGoPrevious = CurrentPage > 1;
        CanGoNext = CurrentPage < TotalPages;

        var aiNote = RankedWithAi ? " · AI ranked" : "";
        ResultsSummary = TotalCount == 0
            ? "No jobs found."
            : $"{TotalCount} result{(TotalCount == 1 ? "" : "s")} · page {CurrentPage}/{TotalPages} · 15 per page{aiNote}";
    }
}

public sealed class FilterOption(string value, string label)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

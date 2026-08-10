using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class JobSearchViewModel : ObservableObject
{
    private const int PageSize = 10;
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
    [ObservableProperty] private string location = "Egypt";
    [ObservableProperty] private FilterOption? selectedExperience;
    [ObservableProperty] private FilterOption? selectedDatePosted;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool hasSearched;
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int totalPages = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private string resultsSummary = string.Empty;

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
            Results.Clear();

            _allResults = await _api.SearchJobsAsync(new JobSearchRequest
            {
                Title = Title.Trim(),
                Location = string.IsNullOrWhiteSpace(Location) ? "Egypt" : Location.Trim(),
                ExperienceLevel = string.IsNullOrWhiteSpace(SelectedExperience?.Value) ? null : SelectedExperience!.Value,
                DatePosted = string.IsNullOrWhiteSpace(SelectedDatePosted?.Value) ? null : SelectedDatePosted!.Value
            });

            TotalCount = _allResults.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
            CurrentPage = 1;
            ApplyPage();
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
        ResultsSummary = TotalCount == 0
            ? "No jobs found."
            : $"{TotalCount} result{(TotalCount == 1 ? "" : "s")} · page {CurrentPage}/{TotalPages}";
    }
}

public sealed class FilterOption(string value, string label)
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

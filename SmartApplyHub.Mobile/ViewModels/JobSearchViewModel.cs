using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class JobSearchViewModel : ObservableObject
{
    private readonly ApiService _api;

    public JobSearchViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string location = "Egypt";
    [ObservableProperty] private string? experienceLevel;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool hasSearched;

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

            var results = await _api.SearchJobsAsync(new JobSearchRequest
            {
                Title = Title.Trim(),
                Location = string.IsNullOrWhiteSpace(Location) ? "Egypt" : Location.Trim(),
                ExperienceLevel = ExperienceLevel
            });

            foreach (var r in results)
                Results.Add(r);
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
    private async Task SelectJobAsync(JobSearchResultDto job)
    {
        var navParams = new Dictionary<string, object> { { "Job", job } };
        await Shell.Current.GoToAsync("JobAnalyze", navParams);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class ApplicationsViewModel : ObservableObject
{
    private readonly ApiService _api;

    public ApplicationsViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int totalPages = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private string filterStatus = "All";
    [ObservableProperty] private bool hasDueFollowUps;

    public List<string> FilterOptions { get; } = ["All", "Applied", "Interviewing", "Offer", "Rejected", "Withdrawn", "NoResponse"];
    public ObservableCollection<ApplicationDto> Applications { get; } = [];
    public ObservableCollection<ApplicationDto> DueFollowUps { get; } = [];

    private List<ApplicationDto> _pageItems = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var resultTask = _api.GetApplicationsAsync(CurrentPage);
            var dueTask = _api.GetDueFollowUpsAsync();
            await Task.WhenAll(resultTask, dueTask);

            var result = await resultTask;
            if (result != null)
            {
                _pageItems = result.Items;
                TotalCount = result.TotalCount;
                TotalPages = result.TotalPages;
                CurrentPage = result.CurrentPage;
                CanGoPrevious = CurrentPage > 1;
                CanGoNext = CurrentPage < TotalPages;
                ApplyFilter();
            }

            DueFollowUps.Clear();
            foreach (var item in await dueTask)
                DueFollowUps.Add(item);
            HasDueFollowUps = DueFollowUps.Count > 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnFilterStatusChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Applications.Clear();
        var filtered = FilterStatus == "All"
            ? _pageItems
            : _pageItems.Where(a => string.Equals(a.PipelineStatus, FilterStatus, StringComparison.OrdinalIgnoreCase));
        foreach (var app in filtered)
            Applications.Add(app);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task UpdatePipelineAsync(ApplicationDto? app)
    {
        if (app == null) return;
        var statuses = new[] { "Applied", "Interviewing", "Offer", "Rejected", "Withdrawn", "NoResponse" };
        var result = await Shell.Current.DisplayActionSheet("Update Pipeline Status", "Cancel", null, statuses);
        if (result == null || result == "Cancel") return;

        try
        {
            await _api.UpdatePipelineAsync(app.Id, result);
            SuccessMessage = "Pipeline status updated.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ScheduleFollowUpAsync(ApplicationDto? app)
    {
        if (app == null) return;
        var choices = new[] { "Tomorrow", "In 3 days", "In 1 week", "Clear reminder" };
        var result = await Shell.Current.DisplayActionSheet("Schedule follow-up", "Cancel", null, choices);
        if (result == null || result == "Cancel") return;

        DateTime? when = result switch
        {
            "Tomorrow" => DateTime.UtcNow.AddDays(1),
            "In 3 days" => DateTime.UtcNow.AddDays(3),
            "In 1 week" => DateTime.UtcNow.AddDays(7),
            _ => null
        };

        try
        {
            await _api.ScheduleFollowUpAsync(app.Id, when);
            SuccessMessage = result == "Clear reminder" ? "Follow-up cleared." : "Follow-up scheduled.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendFollowUpAsync(ApplicationDto? app)
    {
        if (app == null) return;
        var confirm = await Shell.Current.DisplayAlert("Send follow-up", $"Send follow-up email for {app.JobTitle}?", "Send", "Cancel");
        if (!confirm) return;

        try
        {
            IsBusy = true;
            await _api.SendFollowUpAsync(app.Id);
            SuccessMessage = "Follow-up email sent.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

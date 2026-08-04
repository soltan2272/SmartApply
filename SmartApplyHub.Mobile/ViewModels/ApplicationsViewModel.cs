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
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;

    public ObservableCollection<ApplicationDto> Applications { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var result = await _api.GetApplicationsAsync(CurrentPage);
            if (result != null)
            {
                Applications.Clear();
                foreach (var app in result.Items)
                    Applications.Add(app);

                TotalCount = result.TotalCount;
                TotalPages = result.TotalPages;
                CurrentPage = result.CurrentPage;
                CanGoPrevious = CurrentPage > 1;
                CanGoNext = CurrentPage < TotalPages;
            }
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
    private async Task UpdatePipelineAsync(ApplicationDto app)
    {
        var statuses = new[] { "Applied", "Interviewing", "Offer", "Rejected", "Withdrawn", "NoResponse" };
        var result = await Shell.Current.DisplayActionSheet(
            "Update Pipeline Status", "Cancel", null, statuses);

        if (result == null || result == "Cancel") return;

        try
        {
            await _api.UpdatePipelineAsync(app.Id, result);
            app.PipelineStatus = result;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

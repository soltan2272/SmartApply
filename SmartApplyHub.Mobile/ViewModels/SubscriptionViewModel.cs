using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class SubscriptionViewModel : ObservableObject
{
    private readonly ApiService _api;

    public SubscriptionViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private QuotaStatusDto? quota;
    [ObservableProperty] private string selectedPlan = "Pro";
    [ObservableProperty] private string? note;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;

    public ObservableCollection<PlanInfoDto> Plans { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var quotaTask = _api.GetQuotaStatusAsync();
            var plansTask = _api.GetPlansAsync();
            await Task.WhenAll(quotaTask, plansTask);

            Quota = quotaTask.Result;

            Plans.Clear();
            foreach (var p in plansTask.Result)
                Plans.Add(p);
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
    private async Task RequestSubscriptionAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            await _api.RequestSubscriptionAsync(new SubscriptionRequestDto
            {
                Plan = SelectedPlan,
                Note = Note
            });

            SuccessMessage = $"Subscription request for {SelectedPlan} submitted.";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
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
    private async Task RequestTokenResetAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            await _api.RequestTokenResetAsync(new TokenResetRequestDto { Note = Note });
            SuccessMessage = "Token reset request submitted.";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
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

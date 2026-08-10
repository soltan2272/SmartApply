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
    [ObservableProperty] private string pendingSubscriptionText = string.Empty;
    [ObservableProperty] private string pendingTokenResetText = string.Empty;
    [ObservableProperty] private bool hasPendingSubscription;
    [ObservableProperty] private bool hasPendingTokenReset;

    public ObservableCollection<PlanInfoDto> Plans { get; } = [];
    public List<string> UpgradePlans { get; } = ["Pro", "Power"];

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var quotaTask = _api.GetQuotaStatusAsync();
            var plansTask = _api.GetPlansAsync();
            var pendingSubTask = _api.GetPendingSubscriptionAsync();
            var pendingResetTask = _api.GetPendingTokenResetAsync();
            await Task.WhenAll(quotaTask, plansTask, pendingSubTask, pendingResetTask);

            Quota = await quotaTask;

            Plans.Clear();
            foreach (var p in await plansTask)
                Plans.Add(p);

            var pendingSub = await pendingSubTask;
            HasPendingSubscription = pendingSub?.HasPending == true;
            PendingSubscriptionText = HasPendingSubscription
                ? $"Pending upgrade request: {pendingSub!.Request?.RequestedPlan} ({pendingSub.Request?.Status})"
                : string.Empty;

            var pendingReset = await pendingResetTask;
            HasPendingTokenReset = pendingReset?.HasPending == true;
            PendingTokenResetText = HasPendingTokenReset
                ? $"Pending token reset request ({pendingReset!.Request?.Status})"
                : string.Empty;
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
    private void SelectPlan(PlanInfoDto? plan)
    {
        if (plan == null || plan.Plan is "Free") return;
        SelectedPlan = plan.Plan;
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
            await LoadAsync();
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
            await LoadAsync();
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

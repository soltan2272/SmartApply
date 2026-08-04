using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiService _api;
    private readonly AuthService _auth;

    public DashboardViewModel(ApiService api, AuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    [ObservableProperty] private string userEmail = string.Empty;
    [ObservableProperty] private string plan = "Free";
    [ObservableProperty] private int quotaUsed;
    [ObservableProperty] private int quotaLimit;
    [ObservableProperty] private int quotaRemaining;
    [ObservableProperty] private int totalApplications;
    [ObservableProperty] private int totalSent;
    [ObservableProperty] private int pipelineInterviewing;
    [ObservableProperty] private int pipelineOffer;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            UserEmail = _auth.GetEmail() ?? "";

            var quotaTask = _api.GetQuotaStatusAsync();
            var statsTask = _api.GetApplicationStatsAsync();
            await Task.WhenAll(quotaTask, statsTask);

            var quota = quotaTask.Result;
            if (quota != null)
            {
                Plan = quota.Plan;
                QuotaUsed = quota.Used;
                QuotaLimit = quota.Limit;
                QuotaRemaining = quota.Remaining;
            }

            var stats = statsTask.Result;
            if (stats != null)
            {
                TotalApplications = stats.TotalApplications;
                TotalSent = stats.TotalSent;
                PipelineInterviewing = stats.PipelineInterviewing;
                PipelineOffer = stats.PipelineOffer;
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
    private async Task LogoutAsync()
    {
        _auth.Logout();
        Application.Current!.Windows[0].Page = new NavigationPage(
            new Pages.LoginPage(
                Application.Current.Handler!.MauiContext!.Services.GetRequiredService<LoginViewModel>()));
    }
}

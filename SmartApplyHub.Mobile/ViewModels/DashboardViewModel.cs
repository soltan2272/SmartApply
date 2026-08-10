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
    [ObservableProperty] private double quotaProgress;
    [ObservableProperty] private int totalApplications;
    [ObservableProperty] private int totalSent;
    [ObservableProperty] private int pipelineInterviewing;
    [ObservableProperty] private int pipelineOffer;
    [ObservableProperty] private int dueFollowUps;
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
            var dueTask = _api.GetDueFollowUpsAsync();
            await Task.WhenAll(quotaTask, statsTask, dueTask);

            var quota = await quotaTask;
            if (quota != null)
            {
                Plan = quota.Plan;
                QuotaUsed = quota.Used;
                QuotaLimit = quota.Limit;
                QuotaRemaining = quota.Remaining;
                QuotaProgress = quota.Limit <= 0 ? 0 : Math.Clamp(quota.Used / (double)quota.Limit, 0, 1);
            }

            var stats = await statsTask;
            if (stats != null)
            {
                TotalApplications = stats.TotalApplications;
                TotalSent = stats.TotalSent;
                PipelineInterviewing = stats.PipelineInterviewing;
                PipelineOffer = stats.PipelineOffer;
            }

            DueFollowUps = (await dueTask).Count;
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
    private Task GoSearchAsync() => Shell.Current.GoToAsync("//JobSearch");

    [RelayCommand]
    private Task GoAnalyzeAsync() => Shell.Current.GoToAsync("//JobAnalyze");

    [RelayCommand]
    private Task GoApplicationsAsync() => Shell.Current.GoToAsync("//Applications");

    [RelayCommand]
    private Task GoBulkAsync() => Shell.Current.GoToAsync("//BulkApply");

    [RelayCommand]
    private Task LogoutAsync()
    {
        _auth.Logout();
        var services = Application.Current!.Handler!.MauiContext!.Services;
        Application.Current!.Windows[0].Page = new NavigationPage(
            services.GetRequiredService<Pages.LoginPage>());
        return Task.CompletedTask;
    }
}

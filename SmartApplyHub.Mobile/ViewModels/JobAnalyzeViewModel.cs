using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

[QueryProperty(nameof(Job), "Job")]
public partial class JobAnalyzeViewModel : ObservableObject
{
    private readonly ApiService _api;

    public JobAnalyzeViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private JobSearchResultDto? job;
    [ObservableProperty] private string jobUrl = string.Empty;
    [ObservableProperty] private string jobDescription = string.Empty;
    [ObservableProperty] private EmailPreviewDto? emailPreview;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isAnalyzed;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;

    partial void OnJobChanged(JobSearchResultDto? value)
    {
        if (value != null)
        {
            JobUrl = value.Url;
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(JobUrl) && string.IsNullOrWhiteSpace(JobDescription))
        {
            ErrorMessage = "Provide a job URL or paste the description.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var result = await _api.AnalyzeJobAsync(new JobAnalyzeRequest
            {
                JobUrl = string.IsNullOrWhiteSpace(JobUrl) ? null : JobUrl.Trim(),
                JobDescription = string.IsNullOrWhiteSpace(JobDescription) ? null : JobDescription
            });

            EmailPreview = result;
            IsAnalyzed = result != null;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SendEmailAsync()
    {
        if (EmailPreview == null) return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            await _api.SendEmailAsync(new SendEmailRequest
            {
                ToEmail = EmailPreview.ToEmail,
                Subject = EmailPreview.Subject,
                Body = EmailPreview.Body,
                JobTitle = EmailPreview.JobTitle,
                CompanyName = EmailPreview.CompanyName,
                MatchedSkills = EmailPreview.MatchedSkills,
                MatchScore = EmailPreview.MatchScore
            });

            SuccessMessage = $"Email sent to {EmailPreview.ToEmail}!";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

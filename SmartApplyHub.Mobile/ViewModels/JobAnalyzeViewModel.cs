using System.Collections.ObjectModel;
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
    [ObservableProperty] private string toEmail = string.Empty;
    [ObservableProperty] private string subject = string.Empty;
    [ObservableProperty] private string body = string.Empty;
    [ObservableProperty] private string jobTitle = string.Empty;
    [ObservableProperty] private string companyName = string.Empty;
    [ObservableProperty] private int matchScore;
    [ObservableProperty] private bool hasCvOnFile;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isAnalyzed;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;
    [ObservableProperty] private string matchedSkillsText = string.Empty;

    public ObservableCollection<string> MatchedSkills { get; } = [];

    partial void OnJobChanged(JobSearchResultDto? value)
    {
        if (value != null)
            JobUrl = value.Url;
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

            if (result == null)
            {
                ErrorMessage = "Analysis returned no result.";
                return;
            }

            ToEmail = result.ToEmail;
            Subject = result.Subject;
            Body = result.Body;
            JobTitle = result.JobTitle;
            CompanyName = result.CompanyName;
            MatchScore = result.MatchScore;
            HasCvOnFile = result.HasCvOnFile;
            MatchedSkills.Clear();
            foreach (var skill in result.MatchedSkills)
                MatchedSkills.Add(skill);
            MatchedSkillsText = result.MatchedSkills.Count == 0
                ? "No matched skills detected."
                : string.Join(" · ", result.MatchedSkills);
            IsAnalyzed = true;
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
        if (!IsAnalyzed) return;
        if (string.IsNullOrWhiteSpace(ToEmail) || string.IsNullOrWhiteSpace(Subject) || string.IsNullOrWhiteSpace(Body))
        {
            ErrorMessage = "To, Subject, and Body are required before sending.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            await _api.SendEmailAsync(new SendEmailRequest
            {
                ToEmail = ToEmail.Trim(),
                Subject = Subject.Trim(),
                Body = Body,
                JobTitle = JobTitle,
                CompanyName = CompanyName,
                MatchedSkills = MatchedSkills.ToList(),
                MatchScore = MatchScore
            });

            SuccessMessage = $"Email sent to {ToEmail.Trim()}!";
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

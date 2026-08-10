using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class BulkApplyViewModel : ObservableObject
{
    private readonly ApiService _api;

    public BulkApplyViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string sourceText = string.Empty;
    [ObservableProperty] private string recipientsText = string.Empty;
    [ObservableProperty] private string subject = string.Empty;
    [ObservableProperty] private string body = string.Empty;
    [ObservableProperty] private string readinessText = string.Empty;
    [ObservableProperty] private string planInfo = string.Empty;
    [ObservableProperty] private string? dispatchSummary;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;

    public ObservableCollection<string> ExtractedEmails { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var template = await _api.GetBulkTemplateAsync();
            if (template == null) return;

            Subject = template.Subject;
            Body = template.Body;
            PlanInfo = $"{template.Plan} plan · up to {template.BulkMaxRecipients} recipients";

            var checks = new List<string>();
            checks.Add(template.HasCompleteProfile ? "Profile ready" : "Complete profile");
            checks.Add(template.HasCvOnFile ? $"CV: {template.CvFileName}" : "Upload CV");
            checks.Add(template.HasEmailCredential ? "Gmail credential ready" : "Add Gmail App Password");
            ReadinessText = string.Join(" · ", checks);
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
    private async Task ExtractEmailsAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var result = await _api.ExtractEmailsAsync(SourceText);
            ExtractedEmails.Clear();
            foreach (var email in result?.Emails ?? [])
                ExtractedEmails.Add(email);

            if (ExtractedEmails.Count == 0)
            {
                ErrorMessage = "No valid email addresses were found.";
                return;
            }

            var existing = ParseRecipients();
            foreach (var email in ExtractedEmails)
            {
                if (!existing.Contains(email, StringComparer.OrdinalIgnoreCase))
                    existing.Add(email);
            }
            RecipientsText = string.Join(Environment.NewLine, existing);
            SuccessMessage = $"Found {ExtractedEmails.Count} email(s). Review the recipient list below.";
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
    private async Task SaveTemplateAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _api.SaveBulkTemplateAsync(Subject, Body);
            SuccessMessage = "Template saved.";
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
    private async Task ResetTemplateAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _api.ResetBulkTemplateAsync();
            await LoadAsync();
            SuccessMessage = "Template reset to profile defaults.";
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
    private async Task SendAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;
            DispatchSummary = null;

            var response = await _api.BulkSendAsync(new BulkSendRequest
            {
                SourceText = SourceText,
                RecipientEmails = ParseRecipients(),
                Subject = Subject,
                Body = Body
            });

            if (response == null)
            {
                ErrorMessage = "Bulk send failed.";
                return;
            }

            SuccessMessage = $"Queued {response.RecipientCount} applications.";
            var dispatch = await _api.GetBulkDispatchAsync(response.DispatchId);
            if (dispatch != null)
            {
                DispatchSummary =
                    $"Dispatch #{dispatch.Id}: {dispatch.Status} · {dispatch.SentCount}/{dispatch.TotalRecipients} sent · {dispatch.FailedCount} failed";
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

    private List<string> ParseRecipients() =>
        RecipientsText
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

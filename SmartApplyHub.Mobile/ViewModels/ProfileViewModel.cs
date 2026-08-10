using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ApiService _api;

    public ProfileViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string phone = string.Empty;
    [ObservableProperty] private string contactEmail = string.Empty;
    [ObservableProperty] private string skillsSummary = string.Empty;
    [ObservableProperty] private string experienceSummary = string.Empty;
    [ObservableProperty] private string? cvFileName;
    [ObservableProperty] private string senderEmail = string.Empty;
    [ObservableProperty] private string senderName = string.Empty;
    [ObservableProperty] private string appPassword = string.Empty;
    [ObservableProperty] private bool hasEmailCredential;
    [ObservableProperty] private string plan = string.Empty;
    [ObservableProperty] private string quotaSummary = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string successMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var profileTask = _api.GetProfileAsync();
            var quotaTask = _api.GetQuotaStatusAsync();
            await Task.WhenAll(profileTask, quotaTask);

            var profile = await profileTask;
            if (profile != null)
            {
                FullName = profile.FullName;
                Title = profile.Title;
                Phone = profile.Phone;
                ContactEmail = profile.ContactEmail;
                SkillsSummary = profile.SkillsSummary;
                ExperienceSummary = profile.ExperienceSummary;
                CvFileName = profile.CvFile?.FileName;
                HasEmailCredential = profile.EmailCredential?.HasSecret == true;

                if (profile.EmailCredential != null)
                {
                    SenderEmail = profile.EmailCredential.SenderEmail;
                    SenderName = profile.EmailCredential.SenderName;
                }
            }

            var quota = await quotaTask;
            if (quota != null)
            {
                Plan = quota.Plan;
                QuotaSummary = $"{quota.Used}/{quota.Limit} AI used · {quota.Remaining} left";
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
    private async Task SaveProfileAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            await _api.UpdateProfileAsync(new UpdateProfileRequest
            {
                FullName = FullName,
                Title = Title,
                Phone = Phone,
                ContactEmail = ContactEmail,
                SkillsSummary = SkillsSummary,
                ExperienceSummary = ExperienceSummary
            });

            SuccessMessage = "Profile saved.";
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
    private async Task FillFromCvAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var profile = await _api.FillFromCvAsync();
            if (profile != null)
            {
                FullName = string.IsNullOrWhiteSpace(profile.FullName) ? FullName : profile.FullName;
                Title = string.IsNullOrWhiteSpace(profile.Title) ? Title : profile.Title;
                Phone = string.IsNullOrWhiteSpace(profile.Phone) ? Phone : profile.Phone;
                ContactEmail = string.IsNullOrWhiteSpace(profile.ContactEmail) ? ContactEmail : profile.ContactEmail;
                SkillsSummary = profile.SkillsSummary;
                ExperienceSummary = profile.ExperienceSummary;
            }

            SuccessMessage = "Skills and experience filled from your CV.";
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
    private async Task RewriteExperienceAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var rewritten = await _api.RewriteExperienceAsync();
            if (!string.IsNullOrWhiteSpace(rewritten))
                ExperienceSummary = rewritten;

            SuccessMessage = "Experience rewritten in first person.";
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
    private async Task SaveEmailCredentialAsync()
    {
        if (string.IsNullOrWhiteSpace(SenderEmail))
        {
            ErrorMessage = "Sender email is required.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            await _api.UpdateEmailCredentialAsync(new UpdateEmailCredentialRequest
            {
                SenderEmail = SenderEmail,
                SenderName = SenderName,
                Secret = string.IsNullOrWhiteSpace(AppPassword) ? null : AppPassword
            });

            SuccessMessage = "Email credential saved.";
            AppPassword = string.Empty;
            HasEmailCredential = true;
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
    private async Task UploadCvAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select your CV",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/pdf", "application/msword",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
                    { DevicePlatform.WinUI, new[] { ".pdf", ".doc", ".docx" } },
                    { DevicePlatform.iOS, new[] { "com.adobe.pdf", "com.microsoft.word.doc",
                        "org.openxmlformats.wordprocessingml.document" } }
                })
            });

            if (result == null) return;

            IsBusy = true;
            ErrorMessage = string.Empty;

            using var stream = await result.OpenReadAsync();
            await _api.UploadCvAsync(stream, result.FileName, result.ContentType ?? "application/octet-stream");

            CvFileName = result.FileName;
            SuccessMessage = "CV uploaded.";
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
    private async Task DeleteCvAsync()
    {
        try
        {
            IsBusy = true;
            await _api.DeleteCvAsync();
            CvFileName = null;
            SuccessMessage = "CV deleted.";
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

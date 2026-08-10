using System.Net.Http.Json;
using SmartApplyHub.Shared.Models;

namespace SmartApplyHub.Mobile.Services;

public class ApiService
{
    private readonly HttpClient _http;

    public ApiService(HttpClient http)
    {
        _http = http;
    }

    // Auth
    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AuthResponse>();
    }

    public async Task RegisterAsync(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/register", request);
        await EnsureSuccessAsync(response);
    }

    // Jobs
    public async Task<List<JobSearchResultDto>> SearchJobsAsync(JobSearchRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/jobs/search", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<JobSearchResultDto>>() ?? [];
    }

    public async Task<EmailPreviewDto?> AnalyzeJobAsync(JobAnalyzeRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/jobs/analyze", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<EmailPreviewDto>();
    }

    public async Task SendEmailAsync(SendEmailRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/jobs/send", request);
        await EnsureSuccessAsync(response);
    }

    // Applications
    public async Task<PagedResult<ApplicationDto>?> GetApplicationsAsync(int page = 1)
    {
        var response = await _http.GetAsync($"api/applications?page={page}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PagedResult<ApplicationDto>>();
    }

    public async Task<ApplicationStatsDto?> GetApplicationStatsAsync()
    {
        var response = await _http.GetAsync("api/applications/stats");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ApplicationStatsDto>();
    }

    public async Task UpdatePipelineAsync(int applicationId, string pipelineStatus)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/applications/{applicationId}/pipeline",
            new UpdatePipelineRequest { PipelineStatus = pipelineStatus });
        await EnsureSuccessAsync(response);
    }

    public async Task<List<ApplicationDto>> GetDueFollowUpsAsync()
    {
        var response = await _http.GetAsync("api/applications/due-follow-ups");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<ApplicationDto>>() ?? [];
    }

    public async Task ScheduleFollowUpAsync(int applicationId, DateTime? nextFollowUpAt)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/applications/{applicationId}/follow-up",
            new ScheduleFollowUpRequest { NextFollowUpAt = nextFollowUpAt });
        await EnsureSuccessAsync(response);
    }

    public async Task SendFollowUpAsync(int applicationId)
    {
        var response = await _http.PostAsync($"api/applications/{applicationId}/follow-up/send", null);
        await EnsureSuccessAsync(response);
    }

    // Profile
    public async Task<UserProfileDto?> GetProfileAsync()
    {
        var response = await _http.GetAsync("api/profile");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UserProfileDto>();
    }

    public async Task UpdateProfileAsync(UpdateProfileRequest request)
    {
        var response = await _http.PutAsJsonAsync("api/profile", request);
        await EnsureSuccessAsync(response);
    }

    public async Task UpdateEmailCredentialAsync(UpdateEmailCredentialRequest request)
    {
        var response = await _http.PutAsJsonAsync("api/profile/email-credential", request);
        await EnsureSuccessAsync(response);
    }

    public async Task UploadCvAsync(Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        var response = await _http.PostAsync("api/profile/cv", content);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteCvAsync()
    {
        var response = await _http.DeleteAsync("api/profile/cv");
        await EnsureSuccessAsync(response);
    }

    public async Task<UserProfileDto?> FillFromCvAsync()
    {
        var response = await _http.PostAsync("api/profile/fill-from-cv", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UserProfileDto>();
    }

    public async Task<string?> RewriteExperienceAsync()
    {
        var response = await _http.PostAsync("api/profile/rewrite-experience", null);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<RewriteExperienceResponse>();
        return result?.ExperienceSummary;
    }

    // Quota & Billing
    public async Task<QuotaStatusDto?> GetQuotaStatusAsync()
    {
        var response = await _http.GetAsync("api/quota/status");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<QuotaStatusDto>();
    }

    public async Task<List<PlanInfoDto>> GetPlansAsync()
    {
        var response = await _http.GetAsync("api/quota/plans");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<PlanInfoDto>>() ?? [];
    }

    public async Task RequestSubscriptionAsync(SubscriptionRequestDto request)
    {
        var response = await _http.PostAsJsonAsync("api/billing/request-subscription", request);
        await EnsureSuccessAsync(response);
    }

    public async Task RequestTokenResetAsync(TokenResetRequestDto request)
    {
        var response = await _http.PostAsJsonAsync("api/billing/request-token-reset", request);
        await EnsureSuccessAsync(response);
    }

    public async Task<PendingRequestResponse?> GetPendingSubscriptionAsync()
    {
        var response = await _http.GetAsync("api/billing/pending-subscription");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PendingRequestResponse>();
    }

    public async Task<PendingRequestResponse?> GetPendingTokenResetAsync()
    {
        var response = await _http.GetAsync("api/billing/pending-token-reset");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PendingRequestResponse>();
    }

    // Bulk Apply
    public async Task<BulkTemplateDto?> GetBulkTemplateAsync()
    {
        var response = await _http.GetAsync("api/bulk/template");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<BulkTemplateDto>();
    }

    public async Task<ExtractEmailsResponse?> ExtractEmailsAsync(string sourceText)
    {
        var response = await _http.PostAsJsonAsync("api/bulk/extract-emails", new ExtractEmailsRequest { SourceText = sourceText });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ExtractEmailsResponse>();
    }

    public async Task SaveBulkTemplateAsync(string subject, string body)
    {
        var response = await _http.PutAsJsonAsync("api/bulk/template", new SaveBulkTemplateRequest { Subject = subject, Body = body });
        await EnsureSuccessAsync(response);
    }

    public async Task ResetBulkTemplateAsync()
    {
        var response = await _http.DeleteAsync("api/bulk/template");
        await EnsureSuccessAsync(response);
    }

    public async Task<BulkSendResponse?> BulkSendAsync(BulkSendRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/bulk/send", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<BulkSendResponse>();
    }

    public async Task<BulkDispatchDto?> GetBulkDispatchAsync(int id)
    {
        var response = await _http.GetAsync($"api/bulk/dispatch/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<BulkDispatchDto>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            ApiErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            }
            catch
            {
                // Body was empty or not JSON.
            }
            throw new ApiException(error?.Error ?? $"Request failed ({(int)response.StatusCode} {response.StatusCode}).");
        }
    }

    private sealed class RewriteExperienceResponse
    {
        public string ExperienceSummary { get; set; } = string.Empty;
    }
}

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobApplicationBot.Models;
using Microsoft.Extensions.Options;

namespace JobApplicationBot.Services;

public interface IAiService
{
    Task<JobAnalysisResult> AnalyzeJobAsync(string jobDescription);
    Task<EmailPreviewModel> GenerateEmailAsync(JobAnalysisResult analysis, UserProfile profile);
}

public class GeminiAiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;
    private readonly UserProfile _userProfile;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiAiService(HttpClient httpClient, IOptions<AiSettings> settings, IOptions<UserProfile> userProfile)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _userProfile = userProfile.Value;
    }

    public async Task<JobAnalysisResult> AnalyzeJobAsync(string jobDescription)
    {
        var prompt = $$"""
            Analyze the following job posting and extract structured information.
            Return ONLY a valid JSON object with these exact fields (no markdown, no code fences):

            {
              "jobTitle": "the job title",
              "companyName": "company name if found, otherwise 'Unknown'",
              "contactEmail": "contact/application email if found, otherwise empty string",
              "requiredSkills": ["skill1", "skill2"],
              "responsibilities": ["responsibility1", "responsibility2"],
              "experienceLevel": "Junior/Mid/Senior/Lead or description",
              "summary": "2-3 sentence summary of the role"
            }

            Job Posting:
            {{jobDescription}}
            """;

        var responseText = await CallGeminiAsync(prompt);

        try
        {
            var cleaned = CleanJsonResponse(responseText);
            var parsed = JsonSerializer.Deserialize<AiJobAnalysis>(cleaned, JsonOptions);

            return new JobAnalysisResult
            {
                JobTitle = parsed?.JobTitle ?? "Unknown Position",
                CompanyName = parsed?.CompanyName ?? "Unknown Company",
                ContactEmail = parsed?.ContactEmail ?? "",
                RequiredSkills = parsed?.RequiredSkills ?? [],
                Responsibilities = parsed?.Responsibilities ?? [],
                ExperienceLevel = parsed?.ExperienceLevel ?? "Not specified",
                Summary = parsed?.Summary ?? "",
                RawDescription = jobDescription
            };
        }
        catch
        {
            return new JobAnalysisResult
            {
                JobTitle = "Could not parse",
                Summary = responseText,
                RawDescription = jobDescription
            };
        }
    }

    public async Task<EmailPreviewModel> GenerateEmailAsync(JobAnalysisResult analysis, UserProfile profile)
    {
        var prompt = $$"""
            You are writing a professional job application email. 
            
            Candidate Information:
            - Name: {{profile.FullName}}
            - Current Title: {{profile.Title}}
            - Skills: {{profile.SkillsSummary}}
            - Experience: {{profile.ExperienceSummary}}
            - Phone: {{profile.Phone}}
            - Email: {{profile.Email}}
            - LinkedIn: {{profile.LinkedIn}}

            Job Details:
            - Position: {{analysis.JobTitle}}
            - Company: {{analysis.CompanyName}}
            - Required Skills: {{string.Join(", ", analysis.RequiredSkills)}}
            - Key Responsibilities: {{string.Join(", ", analysis.Responsibilities)}}
            - Experience Level: {{analysis.ExperienceLevel}}
            - Summary: {{analysis.Summary}}

            Write a professional, personalized application email that:
            1. Shows genuine interest in this specific role and company
            2. Highlights the candidate's relevant skills that match the job requirements
            3. Briefly mentions relevant experience
            4. Is concise (under 300 words)
            5. Has a professional but warm tone
            6. Mentions that a CV is attached
            7. Ends with professional sign-off including candidate's name, title, phone, email, and LinkedIn

            Return ONLY a JSON object (no markdown, no code fences):
            {
              "subject": "email subject line",
              "body": "the full email body text"
            }
            """;

        var responseText = await CallGeminiAsync(prompt);

        try
        {
            var cleaned = CleanJsonResponse(responseText);
            var parsed = JsonSerializer.Deserialize<AiEmailResult>(cleaned, JsonOptions);

            var matchedSkills = analysis.RequiredSkills
                .Where(skill => profile.SkillsSummary.Contains(skill, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new EmailPreviewModel
            {
                ToEmail = analysis.ContactEmail,
                Subject = parsed?.Subject ?? $"Application for {analysis.JobTitle} at {analysis.CompanyName}",
                Body = parsed?.Body ?? "Could not generate email body. Please write manually.",
                JobTitle = analysis.JobTitle,
                CompanyName = analysis.CompanyName,
                MatchedSkills = matchedSkills,
                CvPath = profile.CvFilePath
            };
        }
        catch
        {
            return new EmailPreviewModel
            {
                ToEmail = analysis.ContactEmail,
                Subject = $"Application for {analysis.JobTitle} at {analysis.CompanyName}",
                Body = responseText,
                JobTitle = analysis.JobTitle,
                CompanyName = analysis.CompanyName,
                CvPath = profile.CvFilePath
            };
        }
    }

    private async Task<string> CallGeminiAsync(string prompt)
    {
        var isGemini = _settings.BaseUrl.Contains("googleapis.com");

        if (isGemini)
        {
            var url = $"{_settings.BaseUrl}/models/{_settings.Model}:generateContent?key={_settings.ApiKey}";
            var geminiBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.7, maxOutputTokens = 2048 }
            };

            var geminiJson = JsonSerializer.Serialize(geminiBody);
            var geminiContent = new StringContent(geminiJson, Encoding.UTF8, "application/json");
            var geminiResponse = await _httpClient.PostAsync(url, geminiContent);
            var geminiResponseString = await geminiResponse.Content.ReadAsStringAsync();

            if (!geminiResponse.IsSuccessStatusCode)
                throw new Exception($"Gemini API error: {geminiResponse.StatusCode} - {geminiResponseString}");

            using var geminiDoc = JsonDocument.Parse(geminiResponseString);
            return geminiDoc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? throw new Exception("Empty response from Gemini");
        }

        // OpenAI-compatible API (GLM, etc.)
        var requestBody = new
        {
            model = _settings.Model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.7,
            max_tokens = 2048
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _settings.BaseUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Content = httpContent;

        var response = await _httpClient.SendAsync(request);
        var responseString = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"AI API error: {response.StatusCode} - {responseString}");

        using var doc = JsonDocument.Parse(responseString);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? throw new Exception("Empty response from AI");
    }

    private static string CleanJsonResponse(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```json"))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];
        if (text.EndsWith("```"))
            text = text[..^3];
        return text.Trim();
    }

    private record AiJobAnalysis(
        [property: JsonPropertyName("jobTitle")] string? JobTitle,
        [property: JsonPropertyName("companyName")] string? CompanyName,
        [property: JsonPropertyName("contactEmail")] string? ContactEmail,
        [property: JsonPropertyName("requiredSkills")] List<string>? RequiredSkills,
        [property: JsonPropertyName("responsibilities")] List<string>? Responsibilities,
        [property: JsonPropertyName("experienceLevel")] string? ExperienceLevel,
        [property: JsonPropertyName("summary")] string? Summary
    );

    private record AiEmailResult(
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("body")] string? Body
    );
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using Microsoft.Extensions.Options;

namespace JobApplicationBot.Services;

public interface IAiService
{
    Task<JobAnalysisResult> AnalyzeJobAsync(string jobDescription, string? overrideApiKey = null, CancellationToken ct = default);

    Task<EmailPreviewModel> GenerateEmailAsync(
        JobAnalysisResult analysis,
        UserProfile profile,
        string? overrideApiKey = null,
        string? cvText = null,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts skills, experience, and basic profile fields from CV plain text.
    /// Experience is always returned in first person.
    /// </summary>
    Task<CvProfileExtractionResult> ExtractProfileFromCvAsync(
        string cvText,
        string? overrideApiKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// Rewrites any profile/experience text into first person (I / my). Never uses he/she/his/her or the person's name as subject.
    /// </summary>
    Task<string> RewriteInFirstPersonAsync(
        string text,
        string? candidateFullName = null,
        string? overrideApiKey = null,
        CancellationToken ct = default);
}

public class GeminiAiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiAiService(HttpClient httpClient, IOptions<AiSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<JobAnalysisResult> AnalyzeJobAsync(string jobDescription, string? overrideApiKey = null, CancellationToken ct = default)
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

        var responseText = await CallGeminiAsync(prompt, overrideApiKey, ct);

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

    public async Task<EmailPreviewModel> GenerateEmailAsync(
        JobAnalysisResult analysis,
        UserProfile profile,
        string? overrideApiKey = null,
        string? cvText = null,
        CancellationToken ct = default)
    {
        var cvSection = string.IsNullOrWhiteSpace(cvText)
            ? "CV text was not available. Rely on the candidate skills/experience fields only."
            : $"""
              Candidate CV excerpt (use this as the primary source of truth for skills and experience):
              {Truncate(cvText, 8000)}
              """;

        var prompt = $$"""
            You are helping the candidate write a job application email as THEMSELVES (first person only).

            Candidate Information:
            - Name: {{profile.FullName}}
            - Current Title: {{profile.Title}}
            - Skills: {{profile.SkillsSummary}}
            - Experience: {{profile.ExperienceSummary}}
            - Phone: {{profile.Phone}}
            - Email: {{profile.ContactEmail}}

            {{cvSection}}

            Job Details:
            - Position: {{analysis.JobTitle}}
            - Company: {{analysis.CompanyName}}
            - Required Skills: {{string.Join(", ", analysis.RequiredSkills)}}
            - Key Responsibilities: {{string.Join(", ", analysis.Responsibilities)}}
            - Experience Level: {{analysis.ExperienceLevel}}
            - Summary: {{analysis.Summary}}

            Subject rules (very important):
            - The subject MUST be ONLY the job title, exactly like: "{{analysis.JobTitle}}"
            - Do NOT write "Application for...", "Applying for...", company name, or any extra words in the subject.

            Body rules (very important):
            1. Write ONLY in first person: "I", "my", "me". NEVER write third person ("he", "she", "his", "her", "they held", "Soltan is...", or the candidate's full name in the body).
            2. If the CV/experience text is written in third person, REWRITE it into first person before using it. Example: "He worked at Orange" → "I worked at Orange".
            3. Do NOT start sentences like "{{profile.FullName}} is a ..." — write "I am a ..." instead.
            4. When mentioning interest in a role, say "Opportunity" in a generic way. Do NOT assume the role type (do not write "Full Stack opportunity", ".NET opportunity", etc.). Prefer phrasing like: "I am interested in this Opportunity."
            5. Match a natural human tone (not generic AI / corporate template language). Avoid clichés like "I am writing to express my strong interest", "great fit", "leverage my expertise".
            6. Highlight relevant skills and experience from the CV that match the job — concrete facts only.
            7. Keep it concise (under 250 words).
            8. Mention that a CV is attached, in a natural short sentence.
            9. Do NOT invent experience or skills that are not in the profile/CV.
            10. Ends with EXACTLY this two-line sign-off and nothing else after it:
               Best regards,
               {{profile.FullName}}
               Do NOT include the candidate's title, phone number, email address, LinkedIn, or any other contact details in the sign-off or anywhere else in the body.

            Return ONLY a JSON object (no markdown, no code fences):
            {
              "subject": "{{analysis.JobTitle}}",
              "body": "the full email body text in first person"
            }
            """;

        var responseText = await CallGeminiAsync(prompt, overrideApiKey, ct);

        try
        {
            var cleaned = CleanJsonResponse(responseText);
            var parsed = JsonSerializer.Deserialize<AiEmailResult>(cleaned, JsonOptions);

            var skillSource = $"{profile.SkillsSummary}\n{cvText}";
            var matchedSkills = analysis.RequiredSkills
                .Where(skill => skillSource.Contains(skill, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var matchScore = ComputeMatchScore(analysis.RequiredSkills, matchedSkills, skillSource);
            analysis.MatchScore = matchScore;

            var subject = NormalizeJobTitleSubject(parsed?.Subject, analysis.JobTitle);

            return new EmailPreviewModel
            {
                ToEmail = analysis.ContactEmail,
                Subject = subject,
                Body = parsed?.Body ?? "Could not generate email body. Please write manually.",
                JobTitle = analysis.JobTitle,
                CompanyName = analysis.CompanyName,
                MatchedSkills = matchedSkills,
                MatchScore = matchScore
            };
        }
        catch
        {
            return new EmailPreviewModel
            {
                ToEmail = analysis.ContactEmail,
                Subject = NormalizeJobTitleSubject(null, analysis.JobTitle),
                Body = responseText,
                JobTitle = analysis.JobTitle,
                CompanyName = analysis.CompanyName,
                MatchScore = analysis.MatchScore
            };
        }
    }

    private static int ComputeMatchScore(List<string> requiredSkills, List<string> matchedSkills, string skillSource)
    {
        if (requiredSkills.Count == 0)
        {
            // Soft signal from experience keywords if JD listed no discrete skills.
            return string.IsNullOrWhiteSpace(skillSource) ? 40 : 55;
        }

        var ratio = (double)matchedSkills.Count / requiredSkills.Count;
        var score = (int)Math.Round(ratio * 100);
        return Math.Clamp(score, 0, 100);
    }

    public async Task<CvProfileExtractionResult> ExtractProfileFromCvAsync(
        string cvText,
        string? overrideApiKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cvText))
            throw new InvalidOperationException("CV text is empty. Upload a readable PDF or DOCX.");

        var prompt = $$"""
            Extract structured candidate profile data from this CV text.
            Return ONLY a valid JSON object with these exact fields (no markdown, no code fences):

            {
              "fullName": "candidate full name if found, otherwise empty string",
              "title": "current or most recent professional title, otherwise empty string",
              "skillsSummary": "comma-separated list of the strongest technical and soft skills (max ~40 skills)",
              "experienceSummary": "2-5 sentence summary of work experience in FIRST PERSON (I / my), highlighting roles, years, and impact",
              "phone": "phone number if found, otherwise empty string",
              "contactEmail": "email if found, otherwise empty string"
            }

            Rules:
            - Do not invent facts not present in the CV.
            - Prefer concise, recruiter-friendly wording.
            - skillsSummary should be a single line of comma-separated skills.
            - experienceSummary MUST be written in FIRST PERSON only ("I worked...", "I currently serve...", "My roles include...").
            - NEVER use third person ("he", "she", "his", "her", or the candidate's full name as the subject of sentences).
            - Bad: "Soltan Salah Abdelhamed is a Senior Developer. He held positions at Orange."
            - Good: "I am a Senior Developer. I held positions at Orange."

            CV Text:
            {{Truncate(cvText, 12000)}}
            """;

        var responseText = await CallGeminiAsync(prompt, overrideApiKey, ct);

        try
        {
            var cleaned = CleanJsonResponse(responseText);
            var parsed = JsonSerializer.Deserialize<AiCvProfile>(cleaned, JsonOptions);
            var experience = Truncate(parsed?.ExperienceSummary?.Trim() ?? string.Empty, 4000);
            var fullName = parsed?.FullName?.Trim() ?? string.Empty;

            // Always force first-person: models often ignore the instruction and write "He/She/Name is...".
            if (!string.IsNullOrWhiteSpace(experience))
            {
                experience = await RewriteInFirstPersonAsync(experience, fullName, overrideApiKey, ct);
                experience = Truncate(experience, 4000);
            }

            return new CvProfileExtractionResult
            {
                FullName = fullName,
                Title = parsed?.Title?.Trim() ?? string.Empty,
                SkillsSummary = Truncate(parsed?.SkillsSummary?.Trim() ?? string.Empty, 2000),
                ExperienceSummary = experience,
                Phone = Truncate(parsed?.Phone?.Trim() ?? string.Empty, 50),
                ContactEmail = Truncate(parsed?.ContactEmail?.Trim() ?? string.Empty, 256)
            };
        }
        catch
        {
            throw new InvalidOperationException("AI could not parse skills and experience from the CV. Try a clearer PDF/DOCX.");
        }
    }

    public async Task<string> RewriteInFirstPersonAsync(
        string text,
        string? candidateFullName = null,
        string? overrideApiKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var nameHint = string.IsNullOrWhiteSpace(candidateFullName)
            ? "the candidate"
            : candidateFullName.Trim();

        var prompt = $$"""
            Rewrite the following text into FIRST PERSON only, as if the candidate is speaking about themselves.

            Candidate name (do NOT use this name in the rewritten text except never as a subject): {{nameHint}}

            Hard rules:
            - Use ONLY: I, my, me, myself.
            - NEVER use: he, she, his, her, him, they (about the candidate), or the candidate's full/partial name as the subject.
            - NEVER start with "{{nameHint}} is..." — start with "I am..." / "I currently..." / "I have...".
            - Keep the same facts, companies, dates, and roles.
            - Keep a natural professional tone.
            - Return ONLY the rewritten plain text. No JSON, no markdown, no quotes.

            Text to rewrite:
            {{Truncate(text, 4000)}}
            """;

        var rewritten = (await CallGeminiAsync(prompt, overrideApiKey, ct)).Trim();
        rewritten = CleanJsonResponse(rewritten).Trim().Trim('"');

        // Safety net if model still returns third person.
        if (LooksLikeThirdPerson(rewritten, candidateFullName))
        {
            rewritten = ForceBasicFirstPerson(rewritten, candidateFullName);
        }

        return rewritten;
    }

    private static bool LooksLikeThirdPerson(string text, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!string.IsNullOrWhiteSpace(fullName)
            && text.Contains(fullName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Common third-person markers at word boundaries.
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"\b(he|she|his|her|him)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string ForceBasicFirstPerson(string text, string? fullName)
    {
        var result = text;
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"\b{System.Text.RegularExpressions.Regex.Escape(fullName)}\b",
                "I",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe is\b", "I am", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe is\b", "I am", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe has\b", "I have", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe has\b", "I have", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe currently\b", "I currently", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe currently\b", "I currently", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe held\b", "I held", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe held\b", "I held", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe began\b", "I began", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe began\b", "I began", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHis\b", "My", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHer\b", "My", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bHe\b", "I", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bShe\b", "I", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bI is\b", "I am", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bI currently serves\b", "I currently serve", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bI holds\b", "I hold", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\bI works\b", "I work", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return result.Trim();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>
    /// Subject should be the job title only (e.g. "Senior Full Stack Developer"),
    /// never "Application for ...".
    /// </summary>
    private static string NormalizeJobTitleSubject(string? aiSubject, string jobTitle)
    {
        var fallback = string.IsNullOrWhiteSpace(jobTitle) ? "Job Opportunity" : jobTitle.Trim();
        if (string.IsNullOrWhiteSpace(aiSubject))
            return fallback;

        var subject = aiSubject.Trim();
        // Strip common prefixes the model may still add.
        foreach (var prefix in new[]
                 {
                     "Application for ",
                     "Applying for ",
                     "Application to ",
                     "Job Application: ",
                     "Re: "
                 })
        {
            if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                subject = subject[prefix.Length..].Trim();
        }

        // If AI added " at Company", keep only the title portion when it matches the known job title.
        if (!string.IsNullOrWhiteSpace(jobTitle)
            && subject.StartsWith(jobTitle, StringComparison.OrdinalIgnoreCase))
        {
            return jobTitle.Trim();
        }

        return string.IsNullOrWhiteSpace(subject) ? fallback : subject;
    }

    private async Task<string> CallGeminiAsync(string prompt, string? overrideApiKey, CancellationToken ct)
    {
        var apiKey = !string.IsNullOrWhiteSpace(overrideApiKey) ? overrideApiKey : _settings.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Gemini API key configured. Set Ai:ApiKey or supply a personal key on your profile.");

        var isGemini = _settings.BaseUrl.Contains("googleapis.com");

        if (isGemini)
        {
            var url = $"{_settings.BaseUrl}/models/{_settings.Model}:generateContent?key={apiKey}";
            var geminiBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.7, maxOutputTokens = 2048 }
            };

            var geminiJson = JsonSerializer.Serialize(geminiBody);
            var geminiContent = new StringContent(geminiJson, Encoding.UTF8, "application/json");
            var geminiResponse = await _httpClient.PostAsync(url, geminiContent, ct);
            var geminiResponseString = await geminiResponse.Content.ReadAsStringAsync(ct);

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
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = httpContent;

        var response = await _httpClient.SendAsync(request, ct);
        var responseString = await response.Content.ReadAsStringAsync(ct);

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

    private record AiCvProfile(
        [property: JsonPropertyName("fullName")] string? FullName,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("skillsSummary")] string? SkillsSummary,
        [property: JsonPropertyName("experienceSummary")] string? ExperienceSummary,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("contactEmail")] string? ContactEmail
    );
}

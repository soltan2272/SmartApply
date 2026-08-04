# External Integrations

The application talks to six external systems.

| # | System | Purpose | Auth |
| --- | --- | --- | --- |
| 1 | Google Gemini REST | Job analysis + email generation | API key (shared or per-user) |
| 2 | LinkedIn `jobs-guest` API | Public job listing search | None (anonymous) |
| 3 | DuckDuckGo HTML | Discovering LinkedIn "hiring" posts | None |
| 4 | Gmail SMTP | Sending the application email | App password (basic SMTP AUTH) |
| 5 | **Google OAuth 2.0** (Phase 1) | External sign-in for application users | OAuth 2.0 web flow |
| 6 | **SQL Server** (Phase 1) | Persistence for Identity + UserProfiles + AiUsages | Trusted connection (LocalDB) or SQL auth |

Application user authentication is provided by ASP.NET Core Identity (cookie-based) with optional Google sign-in. Confidence: **high**.

## 1. Google Gemini

| Aspect | Value |
| --- | --- |
| Service class | [Services/GeminiService.cs](../Services/GeminiService.cs) |
| Settings POCO | `AiSettings` ([Models/AppSettings.cs](../Models/AppSettings.cs) lines 3-8) |
| Config section | `Ai` in [appsettings.json](../appsettings.json) |
| Default base URL | `https://generativelanguage.googleapis.com/v1beta` |
| Default model | `gemini-1.5-flash` |
| Endpoint built | `{BaseUrl}/models/{Model}:generateContent?key={ApiKey}` |
| Auth | API key as `?key=` query parameter. Per-call: `overrideApiKey` parameter takes precedence over `Ai:ApiKey`; this is how a user's `PersonalGeminiApiKey` is plumbed through. Confidence: high. |
| HTTP method | `POST` |
| Generation params | `temperature = 0.7`, `maxOutputTokens = 2048` |

**Request shape (Gemini path)**:

```json
{
  "contents": [{ "parts": [{ "text": "<prompt>" }] }],
  "generationConfig": { "temperature": 0.7, "maxOutputTokens": 2048 }
}
```

**Response handling**: extracts `candidates[0].content.parts[0].text` ([Services/GeminiService.cs](../Services/GeminiService.cs) lines 178-184). The text is then stripped of optional ```` ```json ```` /  ```` ``` ```` fences and deserialized.

**OpenAI-compatible fallback**: If `BaseUrl` does **not** contain `googleapis.com`, the same service falls through to a chat-completions style call ([Services/GeminiService.cs](../Services/GeminiService.cs) lines 188-215):
- `Authorization: Bearer {ApiKey}` header
- `POST {BaseUrl}` with body `{ model, messages: [{ role: "user", content }], temperature, max_tokens }`
- Response parsed as `choices[0].message.content`

This means the same class can talk to Gemini **or** any OpenAI-compatible provider (e.g., GLM, Groq, OpenRouter) by switching `Ai.BaseUrl`. Confidence: **high**.

**Failure mode**: Non-success HTTP responses throw `Exception("Gemini API error: ...")`, which bubbles up to `JobController.Analyze` and is rendered as a `ModelState` error.

**Configuration required**:

```json
"Ai": {
  "ApiKey": "<your key>",
  "Model": "gemini-1.5-flash",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta"
}
```

> **Security note**: Phase 1 removed the leaked key from [appsettings.json](../appsettings.json). The slot is now empty; the working key must be set via User Secrets (`dotnet user-secrets set "Ai:ApiKey" ...`) or environment variables. The original committed key should still be considered compromised and rotated in Google Cloud Console. See [TechnicalDebt.md](TechnicalDebt.md).

## 2. LinkedIn jobs-guest API

| Aspect | Value |
| --- | --- |
| Service class | [Services/JobScraperService.cs](../Services/JobScraperService.cs) (`FetchLinkedInJobs` lines 65-126) |
| Endpoint | `https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search` |
| Method | `GET` |
| Auth | None (anonymous public endpoint) |
| Response | HTML fragment (a list of `<li>` job cards), parsed with HtmlAgilityPack |

**Query parameters built**:

| Param | Source | Notes |
| --- | --- | --- |
| `keywords` | `JobSearchFilter.Title` | URL-encoded |
| `location` | `DefaultLocation = "Egypt"` | Hardcoded constant ([Services/JobScraperService.cs](../Services/JobScraperService.cs) line 15) |
| `start` | `0` | Pagination is not implemented at the API layer |
| `f_TPR` | `JobSearchFilter.DatePosted` | Optional. Values: `r86400`, `r604800`, `r2592000` |
| `f_E` | `JobSearchFilter.ExperienceLevel` | Optional. Values `1`-`6` (Internship → Executive) |

**HTML selectors** (XPath, fragile):

- Title: `.//h3[contains(@class,'base-search-card__title')]`
- Company: `.//h4[contains(@class,'base-search-card__subtitle')]`
- Location: `.//span[contains(@class,'job-search-card__location')]`
- Link: `.//a[contains(@class,'base-card__full-link')]`
- Date: `.//time[contains(@class,'job-search-card__listdate')]` or `.//time`
- Logo: `.//img[contains(@class,'artdeco-entity-image')]` or `.//img`

**User-Agent**: a desktop Chrome string is sent ([Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 21-22) to reduce the chance of being served a different markup variant.

**Failure mode**: Any HTTP failure is silently swallowed and an empty list is returned ([Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 81-88). The user sees "0 results" with no diagnostic.

**ToS / risk**: Scraping LinkedIn against their Terms of Service is a known integration risk; see [TechnicalDebt.md](TechnicalDebt.md).

## 3. DuckDuckGo HTML

| Aspect | Value |
| --- | --- |
| Service class | [Services/JobScraperService.cs](../Services/JobScraperService.cs) (`FetchLinkedInPosts` lines 128-183) |
| Endpoint | `https://html.duckduckgo.com/html/?q={encodedQuery}` |
| Method | `GET` |
| Auth | None |
| Response | HTML SERP, parsed with HtmlAgilityPack |

**Query template**: `site:linkedin.com/posts "{title}" Egypt hiring OR job OR وظيفة`

**Selectors**:

- Result blocks: `//div[contains(@class,'result')]`
- Link: `.//a[contains(@class,'result__a')]`
- Snippet: `.//*[contains(@class,'result__snippet')]`

A result is kept only if its URL contains `linkedin.com/posts`. Author name is parsed from the URL path segment (`/posts/<author-name>/...`).

**Headers added**: `Accept: text/html`, `Accept-Language: en-US,en;q=0.9` ([Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 139-140).

**Failure mode**: Same silent-swallow pattern as LinkedIn (`catch { return results; }`).

## 4. Gmail SMTP

| Aspect | Value |
| --- | --- |
| Service class | [Services/EmailSenderService.cs](../Services/EmailSenderService.cs) |
| Settings POCO | `EmailSettings` ([Models/AppSettings.cs](../Models/AppSettings.cs) lines 10-17) for app-level/Identity email |
| Config section | `Email` in [appsettings.json](../appsettings.json) for app-level/Identity email |
| Per-user settings | `UserEmailCredentials` table for `/BulkApply` |
| Default host | `smtp.gmail.com` |
| Default port | `587` |
| Security | `SecureSocketOptions.StartTls` ([Services/EmailSenderService.cs](../Services/EmailSenderService.cs) line 40) |
| Auth | `AuthenticateAsync(senderEmail, senderPassword)` — Gmail App Password expected |

**Behavior**:

- Builds a `MimeMessage` with plain-text body and (optionally) one in-memory attachment passed as an `EmailAttachment(FileName, ContentType, Content)` record. The bytes come from `UserCvFiles.Content`; nothing is read from the local filesystem.
- `/BulkApply` sends through a per-user SMTP account loaded from `UserEmailCredentials`; the encrypted `Secret` is a Gmail App Password.
- Existing Identity/Forgot Password email can still use app-level `Email:*` settings.
- Connect → Authenticate → Send → Disconnect on every call. There is no SMTP connection pooling.

**Failure mode**: Any MailKit exception bubbles up to `JobController.Send` and is rendered as `"Failed to send email: {ex.Message}"`.

**Configuration required**:

```json
"Email": {
  "SmtpServer": "smtp.gmail.com",
  "SmtpPort": 587,
  "SenderEmail": "you@gmail.com",
  "SenderPassword": "<gmail app password>",
  "SenderName": "Your Name"
}
```

For per-user Gmail sending, users must enable 2-Step Verification and create a 16-character App Password at `https://myaccount.google.com/apppasswords`. Normal Gmail passwords are rejected by Google. Gmail OAuth is the better long-term path because users click "Connect Google" instead of pasting a secret, but it requires Gmail API scopes and refresh-token storage. Confidence: **high**.

## 5. Google OAuth 2.0 (sign-in)

| Aspect | Value |
| --- | --- |
| Configured in | [Program.cs](../Program.cs) (`AddAuthentication().AddGoogle(...)`) |
| Settings | `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret` |
| Callback URL | `/signin-google` (handled by `Microsoft.AspNetCore.Authentication.Google`) |
| Activation | Only registered when both ClientId and ClientSecret are present in configuration. Otherwise the Google button does not appear on the Login page. |

**Setup**:

1. In Google Cloud Console, create an OAuth 2.0 Client ID of type "Web application".
2. Add authorized redirect URI: `https://YOUR_DOMAIN/signin-google` (and `https://localhost:PORT/signin-google` for dev).
3. Store credentials in User Secrets:
   ```
   dotnet user-secrets set "Authentication:Google:ClientId" "..."
   dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
   ```
4. The OAuth consent screen must include the `email` and `profile` scopes (default).

When a user signs in with Google for the first time, ASP.NET Core Identity creates an `AspNetUsers` row and an `AspNetUserLogins` row binding the Google `ProviderKey` to the user.

## 6. SQL Server

| Aspect | Value |
| --- | --- |
| Connection | `ConnectionStrings:DefaultConnection` in [appsettings.json](../appsettings.json) |
| Default | `Server=(localdb)\MSSQLLocalDB;Database=JobApplicationBot;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true` |
| Schema | Managed by EF Core migrations (see [DatabaseStructure.md](DatabaseStructure.md)) |
| Apply schema | `dotnet ef database update` |

Production targets: Azure SQL Database, AWS RDS for SQL Server, or any self-hosted SQL Server 2017+. Confidence: **high**.

## 7. UserProfile (per-user, no longer config)

In Phase 1 the singleton `UserProfile` config section was **removed**. Each user now owns a `UserProfile` row in SQL Server (see [DatabaseStructure.md](DatabaseStructure.md)) that they edit through `/Profile`. CV files are uploaded through the same form and stored as `varbinary(max)` rows in the `UserCvFiles` table (max 5 MB per file). Per-user outbound email credentials are also managed from `/Profile` and stored in `UserEmailCredentials`. At higher scale (millions of users) Phase 3 should move CV bytes to dedicated blob storage to keep DB backups small.

## Configuration Checklist (deployment)

- [ ] `ConnectionStrings:DefaultConnection` (SQL Server)
- [ ] Apply migrations: `dotnet ef database update`
- [ ] `Ai:ApiKey` (shared key for the freemium quota), `Ai:Model`, `Ai:BaseUrl`, `Ai:FreeQuotaPerMonth`
- [ ] `Email:SmtpServer`, `Email:SmtpPort`, `Email:SenderEmail`, `Email:SenderPassword`, `Email:SenderName`
- [ ] Users configure their own Gmail App Password on `/Profile` before using `/BulkApply`
- [ ] (Optional) `Authentication:Google:ClientId` / `ClientSecret` for Google sign-in
- [ ] All secrets in User Secrets (dev) or environment variables / Key Vault (prod), never in `appsettings.json`

---

Last reviewed: 2026-06-01

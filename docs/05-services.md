# 05 – Services

> Location: `Services/` and `Services/Quota/`. Each service has an interface and one implementation, registered in `Program.cs`.

## Table of Contents

- [1. Service Catalog](#1-service-catalog)
- [2. IAiService / GeminiAiService](#2-iaiservice--geminiaiservice)
- [3. IJobScraperService / JobScraperService](#3-ijobscraperservice--jobscraperservice)
- [4. IEmailSenderService / EmailSenderService](#4-iemailsenderservice--emailsenderservice)
- [5. IdentityEmailSender](#5-identityemailsender)
- [6. IUserAccountSetupService / UserAccountSetupService](#6-iuseraccountsetupservice--useraccountsetupservice)
- [7. IAiQuotaService / AiQuotaService](#7-iaiquotaservice--aiquotaservice)
- [8. Supporting Records](#8-supporting-records)

---

## 1. Service Catalog

| Interface | Implementation | Lifetime | External I/O |
|---|---|---|---|
| `IAiService` | `GeminiAiService` | Transient (typed HttpClient) | Gemini / OpenAI-compatible HTTP |
| `IJobScraperService` | `JobScraperService` | Transient (typed HttpClient) | LinkedIn, DuckDuckGo, arbitrary URLs |
| `IEmailSenderService` | `EmailSenderService` | Scoped | Gmail SMTP (MailKit) |
| `IEmailSender<ApplicationUser>` | `IdentityEmailSender` | Transient | Delegates to `IEmailSenderService` |
| `IUserAccountSetupService` | `UserAccountSetupService` | Scoped | DB (via repo) |
| `IAiQuotaService` | `AiQuotaService` | Scoped | DB (via repo) |

---

## 2. IAiService / GeminiAiService

**File:** `Services/GeminiService.cs`

**Responsibility:** Turn a raw job description into structured data, and generate a personalized application email — using Google Gemini (or any OpenAI-compatible chat endpoint).

**Dependencies:** `HttpClient` (typed), `IOptions<AiSettings>`.

**Public methods:**

| Method | Purpose |
|---|---|
| `Task<JobAnalysisResult> AnalyzeJobAsync(string jobDescription, string? overrideApiKey = null, CancellationToken ct = default)` | Prompts the LLM to return JSON with title, company, contact email, skills, responsibilities, experience level, summary. |
| `Task<EmailPreviewModel> GenerateEmailAsync(JobAnalysisResult analysis, UserProfile profile, string? overrideApiKey = null, CancellationToken ct = default)` | Prompts the LLM to write a subject + body tailored to the profile; computes `MatchedSkills`. |

**Business logic / notes:**
- **API key resolution:** `overrideApiKey` (user's personal key) if provided, else `AiSettings.ApiKey`. Throws `InvalidOperationException` if neither is set.
- **Endpoint switch:** if `AiSettings.BaseUrl` contains `googleapis.com` → Google Gemini request shape (`{BaseUrl}/models/{Model}:generateContent?key=...`); otherwise → OpenAI-compatible `chat` shape with `Bearer` auth.
- **Generation config:** `temperature = 0.7`, `maxOutputTokens/max_tokens = 2048`.
- **Response cleaning:** `CleanJsonResponse` strips ```` ```json ```` / ```` ``` ```` fences before `JsonSerializer.Deserialize`.
- **Matched skills:** `analysis.RequiredSkills` filtered to those contained (case-insensitive) in `profile.SkillsSummary`.
- **Sign-off rule:** the email prompt forces a strict two-line `Best regards,\n{FullName}` sign-off and forbids embedding contact details in the body.

**Validation:** none beyond key presence; malformed AI JSON is tolerated (falls back to a raw-text result).

**Repository usage:** none (pure integration service).

**Exception handling:**
- Non-success HTTP → throws `Exception` with status + body.
- JSON parse failures inside `AnalyzeJobAsync`/`GenerateEmailAsync` are caught and converted to a fallback result object (never throws to the caller for parse issues).
- Missing key → `InvalidOperationException` (propagates).

---

## 3. IJobScraperService / JobScraperService

**File:** `Services/JobScraperService.cs`

**Responsibility:** Fetch job description text from a URL, and search LinkedIn jobs + LinkedIn hiring posts.

**Dependencies:** `HttpClient` (typed; sets a desktop Chrome `User-Agent`).

**Public methods:**

| Method | Purpose |
|---|---|
| `Task<string> ScrapeJobDescriptionAsync(string url)` | Downloads the page, strips `script/style/nav/footer/header`, returns cleaned inner text (non-empty trimmed lines). |
| `Task<List<JobSearchResultItem>> SearchJobsAsync(string? title, string? experienceLevel, string? datePosted)` | Runs LinkedIn jobs + LinkedIn posts fetches in parallel and merges results. |

**Private helpers:**
- `FetchLinkedInJobs` — calls the LinkedIn guest jobs API (`jobs-guest/jobs/api/seeMoreJobPostings/search`), location fixed to **"Egypt"**, optional `f_TPR` (date) and `f_E` (experience) filters; parses `<li>` cards with HtmlAgilityPack.
- `FetchLinkedInPosts` — queries `html.duckduckgo.com` for `site:linkedin.com/posts "{title}" Egypt hiring OR job OR وظيفة`; parses result nodes; keeps only `linkedin.com/posts` links; derives author from URL.
- `ExtractAuthorFromUrl`, `CleanPostTitle` — string helpers.

**Business logic / notes:**
- `ResultType` distinguishes `"Job"` vs `"Post"`.
- Snippets truncated to 200 chars; post titles cleaned/truncated to 100 chars.

**Validation:** none (accepts nullable inputs; empty title yields empty encoded query).

**Repository usage:** none.

**Exception handling:** LinkedIn/DuckDuckGo fetch errors are **swallowed** (`try/catch` returns empty list). `ScrapeJobDescriptionAsync` does **not** catch — exceptions propagate to `JobController.Analyze` which handles them.

---

## 4. IEmailSenderService / EmailSenderService

**File:** `Services/EmailSenderService.cs`

**Responsibility:** Send emails over SMTP (MailKit), from either the **global** configured account or a **per-user** account.

**Dependencies:** `IOptions<EmailSettings>`, `ILogger<EmailSenderService>`.

**Public methods:**

| Method | Purpose |
|---|---|
| `SendEmailAsync(string toEmail, string subject, string body, EmailAttachment? attachment = null)` | Send a **text** email using the **global** `EmailSettings` account. Throws if global creds missing. |
| `SendEmailAsync(EmailSenderAccount account, string toEmail, string subject, string body, EmailAttachment? attachment = null)` | Send a **text** email using a **caller-supplied** account (used by Bulk Apply). |
| `SendHtmlEmailAsync(string toEmail, string subject, string htmlBody)` | Send an **HTML** email via the global account (used by Identity confirm/reset). |

**Business logic / notes:**
- All overloads funnel into `SendInternalAsync` which builds a `MimeMessage` (`BodyBuilder` for text and/or HTML + attachment), connects via `SmtpClient` with `StartTls` (or `Auto`), authenticates, sends, disconnects.
- Attachment content type is parsed from `EmailAttachment.ContentType` (fallback `application/octet-stream`).
- Logs an info entry per successful send (From, To, Subject).

**Validation:** global-account overloads throw `InvalidOperationException` if `SenderEmail`/`SenderPassword` are missing.

**Repository usage:** none.

**Exception handling:** SMTP exceptions propagate to callers (`JobController.Send` and `BulkApplyController.Send` catch them; Identity pages catch in some flows).

---

## 5. IdentityEmailSender

**File:** `Services/IdentityEmailSender.cs`

**Responsibility:** Implements `IEmailSender<ApplicationUser>` so ASP.NET Core Identity can send confirmation and password-reset emails.

**Dependencies:** `IEmailSenderService`.

**Public methods:** `SendConfirmationLinkAsync`, `SendPasswordResetLinkAsync`, `SendPasswordResetCodeAsync` — all build a small HTML body branded with `AppBranding.Name` and delegate to `IEmailSenderService.SendHtmlEmailAsync` (global SMTP account).

**Exception handling:** none of its own; SMTP errors bubble up.

---

## 6. IUserAccountSetupService / UserAccountSetupService

**File:** `Services/UserAccountSetupService.cs`

**Responsibility:** Ensure a `UserProfile` row exists for a user and back-fill `ContactEmail`.

**Dependencies:** `IUserProfileRepository`.

**Public method:**
- `EnsureProfileAsync(string userId, string email, CancellationToken ct = default)` — if a profile exists but has no `ContactEmail`, set it and upsert; otherwise create a new profile with only `UserId` + `ContactEmail`.

**Called from:** `RegisterModel.OnPostAsync` and `LoginModel.OnPostAsync`.

**Notes:** Per-user Gmail credentials are **not** created here — only when the user submits them on Profile.

---

## 7. IAiQuotaService / AiQuotaService

**File:** `Services/Quota/AiQuotaService.cs`, `Services/Quota/IAiQuotaService.cs`

**Responsibility:** Enforce the monthly freemium AI quota.

**Dependencies:** `IAiUsageRepository`, `IOptions<AiSettings>`.

**Public methods:**

| Method | Purpose |
|---|---|
| `Task<QuotaCheckResult> GetStatusAsync(string userId, bool hasPersonalKey, CancellationToken ct)` | Read-only current usage vs limit (no state change). Personal key → unlimited. |
| `Task<QuotaCheckResult> TryConsumeAsync(string userId, bool hasPersonalKey, CancellationToken ct)` | Atomically increments usage if below limit; returns whether allowed. Personal key → allowed, not counted. |

**Business logic:**
- `hasPersonalKey == true` → returns `QuotaCheckResult(true, 0, int.MaxValue, BypassedByPersonalKey: true)` and skips DB entirely.
- Otherwise, limit = `AiSettings.FreeQuotaPerMonth`. `TryConsumeAsync` delegates the atomic increment to `AiUsageRepository.TryIncrementIfBelowLimitAsync`.

**Repository usage:** `AiUsageRepository` (`GetCurrentMonthCountAsync`, `TryIncrementIfBelowLimitAsync`).

**Exception handling:** none of its own; concurrency handled inside the repository's retry loop (see [06-data-access.md](06-data-access.md)).

---

## 8. Supporting Records

Defined in `Services/EmailSenderService.cs`:

- `EmailAttachment(string FileName, string ContentType, byte[] Content)` — CV attachment payload.
- `EmailSenderAccount(string SenderEmail, string SenderName, string Secret, string SmtpHost, int SmtpPort, bool UseStartTls)` — per-send SMTP account.

Defined in `Services/Quota/IAiQuotaService.cs`:

- `QuotaCheckResult(bool Allowed, int Used, int Limit, bool BypassedByPersonalKey)` with computed `Remaining => Max(0, Limit - Used)`.

---

_See also: [06-data-access.md](06-data-access.md), [11-background-jobs.md](11-background-jobs.md) (none exist), and [dependency-map.md](dependency-map.md)._

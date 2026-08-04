# Business Rules

This document captures the rules actually present in the source. After Phase 1, **authentication-based rules now apply** (see Section 6 below) and a **freemium quota rule** exists (Section 2.8). Approval workflows and role-based permissions still do not exist and remain marked as Not Applicable.

## 1. Validation Rules

### 1.1 Form input — `EmailPreviewModel` (DataAnnotations)

Source: [Models/EmailPreviewModel.cs](../Models/EmailPreviewModel.cs)

| Field | Rule | Confidence |
| --- | --- | --- |
| `ToEmail` | `[Required(ErrorMessage = "Recipient email is required")]` | high |
| `ToEmail` | `[EmailAddress(ErrorMessage = "Please enter a valid email address")]` | high |
| `Subject` | `[Required(ErrorMessage = "Subject is required")]` | high |
| `Body` | `[Required(ErrorMessage = "Email body is required")]` | high |

These run automatically via ASP.NET model binding and are surfaced by `<span asp-validation-for="...">` in [Views/Job/Preview.cshtml](../Views/Job/Preview.cshtml).

### 1.2 Procedural validation — `JobController.Analyze`

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) lines 40-44

> The user must provide either a `JobUrl` **or** a `JobDescription`. If both are blank, return the Index view with the error `"Please provide either a job URL or description."`.

Confidence: **high**.

### 1.3 Procedural validation — `JobController.Analyze` (post-scrape)

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) lines 55-59

> If the scraper returned an empty/whitespace description, return the Index view with the error `"Could not extract job description. Please paste it manually."`.

Confidence: **high**.

### 1.4 Procedural validation — `JobController.Search` (POST)

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) lines 126-130

> `Filter.Title` is required. If missing, return the Search view with `"Please enter a job title to search."`.

Other filter fields (`ExperienceLevel`, `DatePosted`) are optional. Confidence: **high**.

### 1.5 Implicit validation — CV attachment

Source: [Services/EmailSenderService.cs](../Services/EmailSenderService.cs) lines 32-35

> A CV is attached only if `attachmentPath` is non-empty AND `File.Exists(attachmentPath)` is true. A missing file silently sends an email **without** an attachment — there is no error.

Confidence: **high**. Flagged as a behavior risk in [TechnicalDebt.md](TechnicalDebt.md).

## 2. Business Logic Rules

### 2.1 Skill matching

Source: [Services/GeminiService.cs](../Services/GeminiService.cs) lines 128-130

> A job's `RequiredSkills` are intersected with the user's `UserProfile.SkillsSummary` using **case-insensitive substring containment**, not token equality. A required skill `"C#"` matches a profile string that contains `"c#"` anywhere.

Implication: this is lenient. `"AWS"` would match a profile that mentions `"laws and regulations"` (substring). Confidence: **high**.

### 2.2 CV attachment source

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) `Send` action

> When sending an application, the CV is **always** loaded from the database (`UserCvFiles.Content`) for the authenticated user — never from a path or any value round-tripped through the form. If the user has no CV row, the email is sent without an attachment. Confidence: **high**.

### 2.3 Email subject fallback

Source: [Services/GeminiService.cs](../Services/GeminiService.cs) lines 135 and 148

> If Gemini does not return a parseable subject, default to `"Application for {JobTitle} at {CompanyName}"`.

Confidence: **high**.

### 2.4 Search caching

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) line 144 and lines 106-110

> `JobSearchViewModel` is cached in `IMemoryCache` for **15 minutes** under a freshly-generated `searchId`. Re-issuing `GET /Job/Search?searchId=...&page=N` reads from the cache (no re-scraping).

Confidence: **high**.

### 2.5 Default search location

Source: [Services/JobScraperService.cs](../Services/JobScraperService.cs) line 15

> `DefaultLocation = "Egypt"`. There is no UI to change it — the value is compiled in. The DuckDuckGo query also embeds `"Egypt hiring OR job OR وظيفة"` literally ([Services/JobScraperService.cs](../Services/JobScraperService.cs) line 131).

Confidence: **high**. This is documented as inflexibility in [TechnicalDebt.md](TechnicalDebt.md).

### 2.6 Result deduplication

> There is **no** dedup logic. Identical hits between LinkedIn jobs and DuckDuckGo posts (both pointing to the same LinkedIn URL) appear twice.

Confidence: **high** (verified by inspecting `SearchJobsAsync`, no `Distinct`, `GroupBy`, or set-based merge).

### 2.7 Pagination

Source: [Models/JobSearchViewModel.cs](../Models/JobSearchViewModel.cs)

> Page size is fixed at **10**. `PagedResults` is computed on every read via `Skip/Take`. `TotalCount` is the in-memory list count.

Confidence: **high**.

### 2.8 AI Freemium Quota (Phase 1)

Source: [Services/Quota/AiQuotaService.cs](../Services/Quota/AiQuotaService.cs), [Data/Repositories/AiUsageRepository.cs](../Data/Repositories/AiUsageRepository.cs)

> Each authenticated user without a personal Gemini key is allowed `Ai:FreeQuotaPerMonth` (default 20) Analyze actions per UTC calendar month. Analyze attempts above the limit are rejected with the message `"You've used your N free AI analyses this month..."`. Users with a non-empty `PersonalGeminiApiKey` on their profile bypass the counter entirely. Confidence: **high**.

The increment is concurrency-safe: `TryIncrementIfBelowLimitAsync` retries up to 3 times on `DbUpdateException` (insert race) and `DbUpdateConcurrencyException` (read-modify-write race), backed by the unique index `IX_AiUsages_User_Year_Month`. Confidence: **high**.

### 2.9 CV Upload Constraints (Phase 1)

Source: [Controllers/ProfileController.cs](../Controllers/ProfileController.cs), [Program.cs](../Program.cs)

> Allowed CV extensions: `.pdf`, `.doc`, `.docx`. Maximum content size: **5 MB**. Defense in depth — the limit is enforced at three layers:
>
> 1. **Kestrel** rejects any request body larger than 6 MB with `413 Payload Too Large` (`Limits.MaxRequestBodySize`).
> 2. **Multipart form parser** stops reading at 6 MB (`FormOptions.MultipartBodyLengthLimit`).
> 3. **Controller** returns the form with the friendly model error `"CV file exceeds the 5 MB limit."` for any `IFormFile.Length > 5 * 1024 * 1024`.
>
> The 1 MB headroom between layers 1/2 (6 MB) and layer 3 (5 MB) covers multipart envelope overhead. CVs are stored as `UserCvFiles` rows (one slot per user — re-uploading replaces); the bytes never touch the local filesystem. Confidence: **high**.

### 2.10 Personal Gemini Key Encryption (Phase 1)

Source: [Data/EncryptedStringConverter.cs](../Data/EncryptedStringConverter.cs)

> `UserProfile.PersonalGeminiApiKey` is encrypted at rest using `IDataProtector` with purpose `"JobApplicationBot.UserSecret.v1"`. Plaintext never reaches SQL Server. Confidence: **high**.

> The data-protection keys themselves live on the host's filesystem by default. In a multi-instance deployment, all instances must share the same key ring or stored ciphertext becomes unreadable on the other instance. This is a **Phase 3** concern (deployment).

### 2.11 Bulk Apply Rules

Source: [Controllers/BulkApplyController.cs](../Controllers/BulkApplyController.cs)

> Bulk apply is available only to authenticated users with a complete profile, a CV stored in `UserCvFiles`, and a per-user Gmail SMTP credential stored in `UserEmailCredentials`. The user can send to at most **50 unique valid recipient emails** per submit. The saved CV is attached to every recipient by default.

Template rules:

- Subject and Body are always editable on Bulk Apply. Users can save them to `UserProfiles.BulkTemplateSubject` / `BulkTemplateBody`, or reset to a deterministic default built from `UserProfile.Title`, `SkillsSummary`, `ExperienceSummary`, and `FullName`. No Gemini call is made for the bulk template.
- The auto-generated body ends with exactly `Best regards,` followed by the user's full name.

Confidence: **high**.

## 3. AI-Prompt Rules (instructions to the LLM)

These are encoded as natural-language constraints inside the prompts, not enforced server-side. The model may or may not honor them. Confidence on each item: **medium** — the rule is *asked for*, not *guaranteed*.

### 3.1 Analysis prompt

Source: [Services/GeminiService.cs](../Services/GeminiService.cs) lines 35-51

- Return ONLY a JSON object with exact fields: `jobTitle`, `companyName`, `contactEmail`, `requiredSkills[]`, `responsibilities[]`, `experienceLevel`, `summary`.
- No markdown, no code fences.
- `companyName` should default to `"Unknown"` if not found.
- `contactEmail` should default to empty string if not found.
- `experienceLevel` should be one of `Junior/Mid/Senior/Lead` or a description.
- `summary` should be 2-3 sentences.

Server-side fallback if Gemini disobeys: `CleanJsonResponse` strips fences ([Services/GeminiService.cs](../Services/GeminiService.cs) lines 217-227); on deserialization failure, a stub `JobAnalysisResult` is returned.

### 3.2 Email-generation prompt

Source: [Services/GeminiService.cs](../Services/GeminiService.cs) lines 85-119

The prompt instructs the LLM to produce an email that:

1. Shows genuine interest in the specific role and company.
2. Highlights the candidate's relevant skills matching the job requirements.
3. Briefly mentions relevant experience.
4. Is **under 300 words**.
5. Has a professional but warm tone.
6. Mentions that a CV is attached.
7. Ends with exactly `Best regards,` and the candidate's full name; it explicitly excludes title, phone, email, LinkedIn, and other contact details from the sign-off/body.
8. Returns ONLY a JSON object `{"subject": ..., "body": ...}` (no markdown, no code fences).

Server-side fallback: same `CleanJsonResponse`, then on parse failure the body is set to the raw response text.

## 4. Behavioral Rules from the Pipeline

### 4.1 HTTPS redirection

Source: [Program.cs](../Program.cs) line 32

> All HTTP traffic is redirected to HTTPS via `UseHttpsRedirection()`.

### 4.2 HSTS in production

Source: [Program.cs](../Program.cs) lines 26-30

> When `Environment.IsDevelopment()` is false, `UseHsts()` and `UseExceptionHandler("/Home/Error")` are added to the pipeline.

### 4.3 Kestrel header limit

Source: [Program.cs](../Program.cs) lines 6-9

> `MaxRequestHeadersTotalSize = 64 KB`. Doubled from the default (32 KB). Confidence: **high**. The motivation is not commented; likely a guard for large form posts including hidden `MatchedSkills[]` fields.

## 5. Authorization & Authentication Rules (Phase 1)

### 5.1 All Job and Profile actions require authentication

Source: [Controllers/JobController.cs](../Controllers/JobController.cs), [Controllers/ProfileController.cs](../Controllers/ProfileController.cs) — both classes carry `[Authorize]` at class level.

> Anonymous requests to any `JobController` or `ProfileController` action are intercepted by the cookie auth middleware and redirected to `/Identity/Account/Login` with a `returnUrl`. Confidence: **high**.

### 5.2 Profile completeness gate

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) (`GetOrPromptProfileAsync`).

> Before performing an Analyze or Send action, the controller fetches the user's `UserProfile` row. If it is null, or `FullName` is empty, the user is redirected to `/Profile` with a `TempData["ProfileIncomplete"]` message. The AI generation is gated on a complete profile because the prompt requires the user's name and skills.

### 5.3 Per-user search cache isolation

Source: [Controllers/JobController.cs](../Controllers/JobController.cs) — `searchId = $"{CurrentUserId}:{Guid.NewGuid():N}"`.

> Search cache keys include the user's id, so a leaked or guessed `searchId` from another user cannot return that user's results. Confidence: **high**.

### 5.4 Identity password policy

Source: [Program.cs](../Program.cs) — `AddDefaultIdentity` options.

| Rule | Value |
| --- | --- |
| `Password.RequiredLength` | 8 |
| `Password.RequireNonAlphanumeric` | false |
| `Password.RequireDigit` | true (default) |
| `Password.RequireLowercase` | true (default) |
| `Password.RequireUppercase` | true (default) |
| `User.RequireUniqueEmail` | true |
| `SignIn.RequireConfirmedAccount` | false (Phase 1; email confirmation deferred) |

## 6. Not Applicable to This Codebase

| Subsection | Status | Evidence |
| --- | --- | --- |
| Approval workflows | **Not applicable** | No approval state, no `IsApproved`/`Status` fields, no workflow library. |
| Role-based authorization | **Not applicable** | No `[Authorize(Roles = ...)]` and no roles seeded into `AspNetRoles`. The schema supports it, but it is not used. |
| Permission/claim rules beyond the cookie auth gate | **Not applicable** | No `IAuthorizationService` policies, no claim transformations. |

---

Last reviewed: 2026-06-01

# 04 – API / Endpoint Reference

> This is an **MVC + Razor Pages** app (server-rendered HTML), not a REST/JSON API. Endpoints return **views or redirects**, not JSON. Each controller action is documented below as an endpoint.

## Table of Contents

- [1. Conventions](#1-conventions)
- [2. HomeController](#2-homecontroller)
- [3. JobController](#3-jobcontroller)
- [4. BulkApplyController](#4-bulkapplycontroller)
- [5. ProfileController](#5-profilecontroller)
- [6. Identity Razor Pages](#6-identity-razor-pages)
- [7. Status Codes Summary](#7-status-codes-summary)

---

## 1. Conventions

- **Routing:** default convention `{controller=Home}/{action=Index}/{id?}`.
- **Auth:** `[Authorize]` at controller level unless stated; Identity pages are `[AllowAnonymous]`.
- **CSRF:** all state-changing `POST` actions use `[ValidateAntiForgeryToken]`.
- **Current user id:** resolved from `ClaimTypes.NameIdentifier`.
- **Return type:** HTML view or `RedirectToAction`/`RedirectToPage` (302). No JSON responses.

---

## 2. HomeController

Base route: `/Home` (also the application root `/`). **Not** `[Authorize]`.

### GET `/` or `/Home/Index`
- **Method:** GET
- **Auth:** Anonymous
- **Logic:** If authenticated → `RedirectToAction("Index","Job")`. Else render landing page.
- **Response:** `Views/Home/Index.cshtml` or 302 to `/Job`.

### GET `/Home/Privacy`
- **Method:** GET · **Auth:** Anonymous · **Response:** `Views/Home/Privacy.cshtml`.

> `/Home/Error` is referenced by the production exception handler, but **no `Error` action exists** on `HomeController`. See [code-quality.md](code-quality.md).

---

## 3. JobController

Base route: `/Job`. Controller-level `[Authorize]`.

Dependencies: `IJobScraperService`, `IAiService`, `IEmailSenderService`, `IUserProfileRepository`, `IUserCvFileRepository`, `IAiQuotaService`, `IMemoryCache`.

### GET `/Job` or `/Job/Index`
- **Method:** GET · **Auth:** Authenticated
- **Request model:** none (returns empty `JobInput`)
- **Response:** `Views/Job/Index.cshtml`
- **Status:** 200

### POST `/Job/Analyze`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Request model:** `JobInput { JobUrl?, JobDescription? }`
- **Validation:**
  - At least one of `JobUrl` / `JobDescription` required → else model error, re-render Index.
  - Profile must exist with non-empty `FullName` → else 302 to `/Profile`.
  - Quota must allow (unless personal Gemini key) → else model error.
  - Extracted description must be non-empty → else model error.
- **Business logic:** consume quota → scrape URL (if given) → `IAiService.AnalyzeJobAsync` → `IAiService.GenerateEmailAsync` → set `HasCvOnFile` from CV metadata.
- **Response model:** `EmailPreviewModel` → `Views/Job/Preview.cshtml`
- **DB tables:** `UserProfiles` (read), `UserCvFiles` (metadata read), `AiUsages` (read/write via quota)
- **Related services:** `AiQuotaService`, `JobScraperService`, `GeminiAiService`, `UserCvFileRepository`, `UserProfileRepository`
- **Exceptions:** any exception during scrape/AI is caught → error added to `ModelState`, Index re-rendered.
- **Status:** 200 (Preview / Index), 302 (to Profile)

**Example request (form-encoded):**
```
POST /Job/Analyze
Content-Type: application/x-www-form-urlencoded
__RequestVerificationToken=...&JobUrl=https://example.com/job/123
```
**Example response:** HTML `Preview` view pre-filled with subject, body, matched skills, and recipient email.

### POST `/Job/Send`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Request model:** `EmailPreviewModel` (validates `ToEmail` email, `Subject` required, `Body` required)
- **Validation:** `ModelState.IsValid`; profile must exist (else 302 to Profile).
- **Business logic:** load full CV (bytes) → build optional `EmailAttachment` → `IEmailSenderService.SendEmailAsync` (global SMTP) → stash TempData → 302 `Success`.
- **DB tables:** `UserProfiles` (read), `UserCvFiles` (read bytes)
- **Related services:** `EmailSenderService`, `UserCvFileRepository`, `UserProfileRepository`
- **Exceptions:** send failure caught → model error → re-render `Preview`.
- **Status:** 302 (Success) or 200 (Preview on error)

### GET `/Job/Success`
- **Method:** GET · **Auth:** Authenticated · **Response:** `Views/Job/Success.cshtml` (reads TempData recipient/job/company).

### GET `/Job/Search`
- **Method:** GET · **Auth:** Authenticated
- **Query params:** `searchId?` (cache key), `page` (default 1)
- **Logic:** if `searchId` found in `IMemoryCache`, return cached `JobSearchViewModel` at requested page; else empty view model.
- **Response:** `Views/Job/Search.cshtml`

### POST `/Job/Search`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Request model:** `JobSearchFilter { Title, ExperienceLevel?, DatePosted? }`
- **Validation:** `Title` required → else model error.
- **Business logic:** `IJobScraperService.SearchJobsAsync(title, experienceLevel, datePosted)` → cache result 15 min under `{userId}:{guid}`.
- **Response model:** `JobSearchViewModel` → `Views/Job/Search.cshtml`
- **DB tables:** none
- **Exceptions:** search failure caught → model error; empty results.
- **Status:** 200

---

## 4. BulkApplyController

Base route: `/BulkApply`. Controller-level `[Authorize]`.

Dependencies: `IUserProfileRepository`, `IUserCvFileRepository`, `IUserEmailCredentialRepository`, `IEmailSenderService`.

### GET `/BulkApply` or `/BulkApply/Index`
- **Method:** GET · **Auth:** Authenticated
- **Logic:** builds `BulkApplyViewModel` with prerequisite flags (`HasCvOnFile`, `HasEmailCredential`, `HasCompleteProfile`), editable subject/body prefilled from saved template or profile default, and one empty recipient row.
- **DB tables:** `UserProfiles`, `UserCvFiles` (metadata), `UserEmailCredentials` (read)
- **Response:** `Views/BulkApply/Index.cshtml`

### POST `/BulkApply/SaveTemplate`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Logic:** saves `Subject` / `Body` to `UserProfiles.BulkTemplateSubject` / `BulkTemplateBody`.
- **Response:** redirect to Index with success message.

### POST `/BulkApply/ResetTemplate`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Logic:** clears saved template fields; next Index load uses profile-based default.
- **Response:** redirect to Index.

### POST `/BulkApply/Send`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Request model:** `BulkApplyViewModel { RecipientEmails[], Subject, Body, SourceText? }`
- **Validation:**
  - Recipients: ≥1, ≤ `MaxRecipients` (500), each valid email, duplicates removed (case-insensitive).
  - `Subject` and `Body` required.
  - Prerequisites: complete profile, CV on file, saved Gmail App Password.
- **Business logic:** enqueue bulk dispatch with form subject/body via `IApplicationTrackingService`.
- **DB tables:** `UserProfiles`, `UserCvFiles`, `UserEmailCredentials`, `BulkEmailDispatches`, `JobApplications`
- **Response:** redirect to `Application/Dispatch/{id}`.

---

## 5. ProfileController

Base route: `/Profile`. Controller-level `[Authorize]`.

Dependencies: `IUserProfileRepository`, `IUserCvFileRepository`, `IUserEmailCredentialRepository`, `IAiQuotaService`, `UserManager<ApplicationUser>`.

### GET `/Profile` or `/Profile/Index`
- **Method:** GET · **Auth:** Authenticated
- **Logic:** load profile, CV metadata, email credential, quota status → build `ProfileViewModel`.
- **DB tables:** `UserProfiles`, `UserCvFiles` (metadata), `UserEmailCredentials`, `AiUsages` (read), `AspNetUsers`
- **Response:** `Views/Profile/Index.cshtml`

### POST `/Profile/Index`
- **Method:** POST · **Auth:** Authenticated · **CSRF:** required
- **Request model:** `ProfileViewModel`
- **Validation:**
  - `ModelState` (FullName required, string length limits, email formats).
  - CV upload: ≤ 5 MB; extension ∈ `.pdf/.doc/.docx`.
  - If saving a Gmail App Password without a `SenderEmail` → model error.
- **Business logic:** upsert CV (if uploaded) → upsert `UserProfile` → upsert `UserEmailCredential` (if sender email or app password provided) → TempData success → 302 to Index (PRG pattern).
- **DB tables:** `UserCvFiles` (write), `UserProfiles` (write), `UserEmailCredentials` (write)
- **Related services:** repositories, `AiQuotaService` (status on re-render)
- **Status:** 302 (success) / 200 (validation errors)

### GET `/Profile/Cv`
- **Method:** GET · **Auth:** Authenticated
- **Logic:** return the stored CV as a file download; `Cache-Control: private, no-store`.
- **DB tables:** `UserCvFiles` (bytes)
- **Response:** `FileResult` (200) or `404 NotFound` if no CV.

---

## 6. Identity Razor Pages

Area `Identity`, path prefix `/Identity/Account/...`. All `[AllowAnonymous]`.

| Page | Methods | Purpose |
|---|---|---|
| `/Identity/Account/Register` | GET, POST | Create account, send confirmation email, ensure profile |
| `/Identity/Account/RegisterConfirmation` | GET | Post-register info page |
| `/Identity/Account/ConfirmEmail` | GET | Confirm email via token |
| `/Identity/Account/Login` | GET, POST | Sign in; on success ensure profile + prompt for Gmail App Password |
| `/Identity/Account/Logout` | POST | Sign out |
| `/Identity/Account/ForgotPassword` | GET, POST | Send reset link |
| `/Identity/Account/ForgotPasswordConfirmation` | GET | Info page |
| `/Identity/Account/ResetPassword` | GET, POST | Set new password from token |
| `/Identity/Account/ResetPasswordConfirmation` | GET | Info page |
| `/Identity/Account/ResendEmailConfirmation` | GET, POST | Resend confirmation email |

Details of the auth flows: [07-security.md](07-security.md) and [features/Authentication.md](features/Authentication.md).

---

## 7. Status Codes Summary

| Code | When |
|---|---|
| 200 | View rendered successfully (or file download) |
| 302 | Redirects (PRG, auth challenge to Login, Home→Job, Analyze→Profile, success pages) |
| 401/redirect | Unauthenticated access to `[Authorize]` action → redirect to Login (`ConfigureApplicationCookie`) |
| 404 | `GET /Profile/Cv` when no CV stored; `ConfirmEmail` unknown user |
| 400 | Antiforgery/model-binding failures at framework level |
| 413 (implicit) | Request body over 6 MB is rejected by Kestrel |
| 500 | Unhandled exception → dev page or `/Home/Error` |

---

_See also: [10-dtos.md](10-dtos.md) for request/response model details and [09-business-flows.md](09-business-flows.md) for sequence diagrams._

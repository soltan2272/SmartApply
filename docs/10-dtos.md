# 10 – DTOs, View Models & Options

> Location: `Models/`. This project does not separate "DTOs" from "view models" — the classes in `Models/` serve as request models, view models, and integration DTOs. Entities are documented in [03-database.md](03-database.md).

## Table of Contents

- [1. Overview](#1-overview)
- [2. JobInput](#2-jobinput)
- [3. JobAnalysisResult](#3-jobanalysisresult)
- [4. EmailPreviewModel](#4-emailpreviewmodel)
- [5. ProfileViewModel](#5-profileviewmodel)
- [6. BulkApplyViewModel / BulkApplySendResult](#6-bulkapplyviewmodel--bulkapplysendresult)
- [7. JobSearchFilter](#7-jobsearchfilter)
- [8. JobSearchResultItem](#8-jobsearchresultitem)
- [9. JobSearchViewModel](#9-jobsearchviewmodel)
- [10. ErrorViewModel](#10-errorviewmodel)
- [11. Options Classes (AiSettings, EmailSettings)](#11-options-classes-aisettings-emailsettings)
- [12. AppBranding](#12-appbranding)
- [13. Service Records (EmailAttachment, EmailSenderAccount, QuotaCheckResult, CvFileMetadata)](#13-service-records)
- [14. Identity InputModels](#14-identity-inputmodels)

---

## 1. Overview

| Model | Purpose | Used by | Related entity |
|---|---|---|---|
| `JobInput` | Analyze request | `POST /Job/Analyze` | — |
| `JobAnalysisResult` | AI analysis output | `GeminiAiService` → `JobController` | — |
| `EmailPreviewModel` | Email preview + send | `Job/Preview`, `POST /Job/Send` | `UserCvFile` (flag) |
| `ProfileViewModel` | Profile form | `Profile/Index` GET/POST | `UserProfile`, `UserCvFile`, `UserEmailCredential`, `AiUsage` |
| `BulkApplyViewModel` | Bulk apply form | `BulkApply/Index`, `POST /BulkApply/Send` | `UserProfile`, `UserCvFile`, `UserEmailCredential` |
| `JobSearchFilter` | Search criteria | `POST /Job/Search` | — |
| `JobSearchResultItem` | One search result | `JobScraperService`, Search view | — |
| `JobSearchViewModel` | Search page state + paging | `Job/Search` | — |
| `ErrorViewModel` | Error page | `Views/Shared/Error.cshtml` | — |

---

## 2. JobInput

| Property | Type | Validation | Notes |
|---|---|---|---|
| `JobUrl` | `string?` | — | `[Display("Job URL")]` |
| `JobDescription` | `string?` | — | `[Display("Job Description")]` |

- **Rule (controller-level):** at least one of the two must be provided.
- **APIs:** `POST /Job/Analyze`.

## 3. JobAnalysisResult

| Property | Type | Notes |
|---|---|---|
| `JobTitle` | `string` | |
| `CompanyName` | `string` | |
| `ContactEmail` | `string` | drives `EmailPreviewModel.ToEmail` |
| `RequiredSkills` | `List<string>` | |
| `Responsibilities` | `List<string>` | |
| `ExperienceLevel` | `string` | |
| `Summary` | `string` | |
| `RawDescription` | `string` | original scraped/pasted text |

- Produced by `GeminiAiService.AnalyzeJobAsync`; not bound from a form.

## 4. EmailPreviewModel

| Property | Type | Validation | Notes |
|---|---|---|---|
| `ToEmail` | `string` | `[Required]`, `[EmailAddress]`, `[Display("To")]` | prefilled from analysis contact email |
| `Subject` | `string` | `[Required]`, `[Display("Subject")]` | |
| `Body` | `string` | `[Required]`, `[Display("Email Body")]` | |
| `JobTitle` | `string` | — | display |
| `CompanyName` | `string` | — | display |
| `MatchedSkills` | `List<string>` | — | skills ∩ profile skills |
| `HasCvOnFile` | `bool` | — | set by controller, not bound |

- **APIs:** rendered by `Analyze`; posted to `POST /Job/Send`.

## 5. ProfileViewModel

| Property | Type | Validation | Notes |
|---|---|---|---|
| `FullName` | `string` | `[Required]`, `[StringLength(200)]` | |
| `Title` | `string` | `[StringLength(200)]` | |
| `Phone` | `string` | `[StringLength(50)]` | |
| `ContactEmail` | `string` | `[EmailAddress]`, `[StringLength(256)]` | shown in application emails |
| `SkillsSummary` | `string` | `[StringLength(2000)]` | |
| `ExperienceSummary` | `string` | `[StringLength(4000)]` | |
| `PersonalGeminiApiKey` | `string?` | `[StringLength(2000)]` | optional; bypasses quota |
| `HasPersonalGeminiApiKey` | `bool` | — | display flag |
| `SenderEmail` | `string` | `[EmailAddress]`, `[StringLength(256)]` | Gmail sender |
| `SenderName` | `string` | `[StringLength(200)]` | |
| `SenderAppPassword` | `string?` | `[StringLength(2000)]`, `[DataType(Password)]` | Gmail App Password |
| `HasEmailCredential` | `bool` | — | display flag |
| `CvUpload` | `IFormFile?` | — | validated in controller (≤5MB, ext) |
| `ExistingCvFileName` | `string?` | — | display |
| `ExistingCvSizeBytes` | `long?` | — | display |
| `QuotaUsed` | `int` | — | display |
| `QuotaLimit` | `int` | — | display (0 when personal key) |
| `QuotaRemaining` | `int` (computed) | — | `Max(0, Limit - Used)` |

- **APIs:** `Profile/Index` GET/POST.

## 6. BulkApplyViewModel / BulkApplySendResult

`BulkApplyViewModel`:

| Property | Type | Validation | Notes |
|---|---|---|---|
| `MaxRecipients` | `const int = 500` | — | cap |
| `RecipientEmails` | `List<string>` | controller: ≤50, valid, distinct | starts with one empty row |
| `UseCustomTemplate` | removed — subject/body are always editable |
| `Subject` | `string` | `[Required]`, `[StringLength(300)]` | prefilled from saved template or profile default |
| `Body` | `string` | `[Required]`, `[StringLength(5000)]` | prefilled from saved template or profile default |
| `HasSavedTemplate` | `bool` | — | display |
| `DefaultSubjectPreview` | `string` | — | profile default (for reset) |
| `DefaultBodyPreview` | `string` | — | profile default (for reset) |
| `HasCvOnFile` | `bool` | — | prerequisite flag |
| `CvFileName` | `string?` | — | display |
| `HasEmailCredential` | `bool` | — | prerequisite flag |
| `HasCompleteProfile` | `bool` | — | prerequisite flag |
| `Results` | `List<BulkApplySendResult>` | — | per-recipient outcomes |

`BulkApplySendResult`: `Email` (string), `Success` (bool), `Message` (string).

## 7. JobSearchFilter

| Property | Type | Notes |
|---|---|---|
| `Title` | `string?` | required (controller) |
| `ExperienceLevel` | `string?` | LinkedIn `f_E` code |
| `DatePosted` | `string?` | LinkedIn `f_TPR` code |

## 8. JobSearchResultItem

| Property | Type | Notes |
|---|---|---|
| `Title` | `string` | |
| `Company` | `string` | |
| `Location` | `string` | |
| `Url` | `string` | |
| `DatePosted` | `string` | |
| `LogoUrl` | `string?` | |
| `Snippet` | `string?` | posts only |
| `ResultType` | `string` | `"Job"` or `"Post"` |

## 9. JobSearchViewModel

| Property | Type | Notes |
|---|---|---|
| `SearchId` | `string?` | cache key `{userId}:{guid}` |
| `Filter` | `JobSearchFilter` | |
| `Results` | `List<JobSearchResultItem>` | full result set |
| `HasSearched` | `bool` | |
| `CurrentPage` | `int` (default 1) | |
| `PageSize` | `int` (default 10) | |
| `TotalCount` / `TotalPages` | computed | |
| `PagedResults` | computed | `Skip/Take` slice |

## 10. ErrorViewModel

| Property | Type | Notes |
|---|---|---|
| `RequestId` | `string?` | |
| `ShowRequestId` | `bool` (computed) | true when RequestId set |

## 11. Options Classes (AiSettings, EmailSettings)

See [08-configuration.md](08-configuration.md#4-options-classes). `AiSettings` (`Ai` section) and `EmailSettings` (`Email` section) are bound via `IOptions<T>`.

## 12. AppBranding

Static constants in `Models/AppBranding.cs`: `Name = "SmartApply Hub"`, `Tagline`, `FooterCredit = "Powered by Soltan Salah"`. Used across views and Identity emails.

## 13. Service Records

Defined alongside services (not in `Models/`):

| Record | Location | Purpose |
|---|---|---|
| `EmailAttachment(FileName, ContentType, Content)` | `EmailSenderService.cs` | Email attachment payload |
| `EmailSenderAccount(SenderEmail, SenderName, Secret, SmtpHost, SmtpPort, UseStartTls)` | `EmailSenderService.cs` | Per-send SMTP account |
| `QuotaCheckResult(Allowed, Used, Limit, BypassedByPersonalKey)` | `IAiQuotaService.cs` | Quota decision + `Remaining` |
| `CvFileMetadata(FileName, ContentType, SizeBytes, UploadedAt)` | `IUserCvFileRepository.cs` | CV metadata projection |

## 14. Identity InputModels

Each Identity Razor Page defines a nested `InputModel` (bound with `[BindProperty]`):

- **Login:** `Email` (`[Required][EmailAddress]`), `Password` (`[Required][Password]`), `RememberMe`.
- **Register:** `Email`, `Password` (`[StringLength(100, MinimumLength=8)]`), `ConfirmPassword` (`[Compare]`).
- **ForgotPassword / ResetPassword / ResendEmailConfirmation:** email (+ password fields for reset).

---

_See also: [03-database.md](03-database.md) for entities and [04-api-reference.md](04-api-reference.md) for endpoint usage._

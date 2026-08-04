# API Inventory

This is an MVC application with Razor Pages for Identity. There are no JSON Web API endpoints. Endpoints below return Razor views, redirects, or re-render the same view with `ModelState` errors.

## Routing

Default route ([Program.cs](../Program.cs)):

```
{controller=Home}/{action=Index}/{id?}
```

Razor Pages are also mapped (`app.MapRazorPages()`) for the Identity UI under `/Identity/Account/*`.

## Authorization

| Controller | Authorization |
| --- | --- |
| `HomeController` | None (used by anonymous users to land on the login page) |
| `JobController` | `[Authorize]` at class level — every Job action requires a signed-in user |
| `ProfileController` | `[Authorize]` at class level |
| `BulkApplyController` | `[Authorize]` at class level |
| Identity Razor Pages | `[AllowAnonymous]` for Register/Login; `[Authorize]` for Manage |

Unauthenticated users hitting an `[Authorize]` endpoint are redirected to `/Identity/Account/Login` (configured in `ConfigureApplicationCookie`). Confidence: high.

## Endpoint Summary

| # | Verb | Path | Action | Auth | Returns |
| --- | --- | --- | --- | --- | --- |
| 1 | GET | `/` | `HomeController.Index` | Anon | 302 → `Job/Index` if signed in, else `/Identity/Account/Login` |
| 2 | GET | `/Job` (or `/Job/Index`) | `JobController.Index` | Required | View `Index` with empty `JobInput` |
| 3 | POST | `/Job/Analyze` | `JobController.Analyze` | Required + complete profile + quota | View `Preview` with `EmailPreviewModel`, or re-renders `Index` with errors |
| 4 | POST | `/Job/Send` | `JobController.Send` | Required | 302 → `Success`, or re-renders `Preview` |
| 5 | GET | `/Job/Success` | `JobController.Success` | Required | View `Success` |
| 6 | GET | `/Job/Search` | `JobController.Search` (GET) | Required | View `Search` (cached or empty) |
| 7 | POST | `/Job/Search` | `JobController.Search` (POST) | Required | View `Search` with results |
| 8 | GET | `/Profile` | `ProfileController.Index` (GET) | Required | View `Index` with `ProfileViewModel` |
| 9 | POST | `/Profile` | `ProfileController.Index` (POST) | Required | 302 → `Profile/Index` on success, or re-renders form |
| 10 | GET | `/BulkApply` | `BulkApplyController.Index` | Required | Bulk CV apply form |
| 11 | POST | `/BulkApply/Send` | `BulkApplyController.Send` | Required + complete profile + CV + email credential | Re-renders form with per-recipient send results |
| 12 | GET | `/Identity/Account/Register` | Razor Page | Anon | Register form |
| 13 | POST | `/Identity/Account/Register` | Razor Page | Anon | Creates user, signs in, redirects to home |
| 14 | GET | `/Identity/Account/Login` | Razor Page | Anon | Login form (with Google button when configured) |
| 15 | POST | `/Identity/Account/Login` | Razor Page | Anon | Issues auth cookie |
| 16 | POST | `/Identity/Account/Logout` | Razor Page | Required | Clears auth cookie |
| 17 | GET | `/Identity/Account/Manage` | Razor Page | Required | Identity self-service (change password, etc.) |
| 18 | GET | `/signin-google` | OAuth callback (Authentication.Google) | Anon | Completes external login flow |

## Endpoint Details

### `POST /Job/Analyze`

- **Action**: [Controllers/JobController.cs](../Controllers/JobController.cs)
- **Request model**: [`JobInput`](../Models/JobInput.cs) (form-encoded)
- **Anti-forgery**: enforced via `[ValidateAntiForgeryToken]`.
- **Pre-conditions**:
  1. User is authenticated.
  2. `IUserProfileRepository.GetByUserIdAsync(userId)` returns a non-null profile with a non-empty `FullName`. Otherwise the user is redirected to `/Profile` with a `TempData["ProfileIncomplete"]` warning.
  3. Quota check via `IAiQuotaService.TryConsumeAsync(userId, hasPersonalKey)`. If the user has a `PersonalGeminiApiKey`, the check is bypassed; otherwise the monthly counter is atomically incremented if under `Ai:FreeQuotaPerMonth`. Exhaustion returns the Index view with: `"You've used your N free AI analyses this month. Add your own Gemini API key on the Profile page to continue."`
- **Pipeline**: scraper (if URL) → `IAiService.AnalyzeJobAsync(desc, overrideKey?)` → `IAiService.GenerateEmailAsync(analysis, profile, overrideKey?)`.
- **Response**: View `Preview` with `EmailPreviewModel`.

### `POST /Job/Send`

- **Anti-forgery**: enforced.
- **Pre-conditions**: authenticated; profile present.
- **CV attachment**: the controller fetches the user's `UserCvFile` from the database via `IUserCvFileRepository.GetAsync(userId)`. If a row exists, the bytes are wrapped in an `EmailAttachment(FileName, ContentType, Content)` and forwarded to `IEmailSenderService.SendEmailAsync`. There is **no** server-side path round-tripped through the form: `EmailPreviewModel.CvPath` was removed; the form now only carries the read-only flag `HasCvOnFile` (set by the controller for display purposes).

### `POST /Job/Search`

- **Search ID**: now scoped per user (`{userId}:{guid}`) so cached entries from one user are not served to another. Confidence: high.

### `GET /Profile`

- **Returns** [`ProfileViewModel`](../Models/ProfileViewModel.cs) populated with current profile + quota status. `ContactEmail` defaults to the user's auth email if no profile row exists yet.

### `POST /Profile`

- **Multipart** (`enctype="multipart/form-data"`) — the `CvUpload` field is an `IFormFile`.
- **Validation**:
  - `FullName` required.
  - `ContactEmail` is `[EmailAddress]`.
  - `CvUpload` (if provided) must be `.pdf`, `.doc`, or `.docx` and ≤ **5 MB**.
  - `SenderEmail` is `[EmailAddress]`; `SenderAppPassword` is optional on later saves and replaces the encrypted saved App Password only when supplied.
- **Server-side limits** (defense in depth — see [Program.cs](../Program.cs)):
  - Kestrel `MaxRequestBodySize` = 6 MB (rejected with `413 Payload Too Large` before the action runs).
  - `FormOptions.MultipartBodyLengthLimit` = 6 MB.
  - The 1 MB headroom over the 5 MB content cap covers multipart envelope overhead.
- **Side effects**:
  - On valid submission with a CV file, the bytes are read into memory and **upserted into `UserCvFiles`** via `IUserCvFileRepository.UpsertAsync`. Existing CV rows are replaced; deleting the user cascades to their CV.
  - `PersonalGeminiApiKey` is stored encrypted at rest (DataProtection).
  - Per-user Gmail SMTP sender settings are upserted into `UserEmailCredentials`; `Secret` is encrypted at rest.
- **Response**: 302 → `/Profile` with `TempData["ProfileSaved"] = "Profile saved."`.

### `GET /Profile/Cv`

- **Auth**: `[Authorize]` (inherited).
- **Action**: [Controllers/ProfileController.cs](../Controllers/ProfileController.cs) → `Cv`.
- **Behavior**: streams the current user's CV (`UserCvFiles.Content`) back as a file download with `Content-Type` and original `FileName`. Adds `Cache-Control: private, no-store` so the response is never cached on shared proxies.
- **404** when no CV row exists for the user.
- Used by the "Current CV" download link on `/Profile` and by users wanting to verify what they have on file.

### `GET /BulkApply`

- **Auth**: `[Authorize]`.
- **Returns** [`BulkApplyViewModel`](../Models/BulkApplyViewModel.cs) with profile/CV/email-sender readiness flags, recipient rows, and a preview of the auto-generated profile-based template.

### `POST /BulkApply/Send`

- **Anti-forgery**: enforced.
- **Pre-conditions**:
  - Authenticated user.
  - Complete profile (`FullName`, `Title`, `SkillsSummary`, `ExperienceSummary`).
  - CV row exists in `UserCvFiles`.
  - Per-user email credential exists in `UserEmailCredentials` with an encrypted App Password.
  - 1-50 unique, valid recipient emails.
- **Template behavior**:
  - Subject and Body are always required and sent unchanged to every recipient.
  - Prefill from saved profile template when present; otherwise build a deterministic subject/body from the user's profile (no Gemini / no AI quota).
  - `POST /BulkApply/SaveTemplate` persists subject/body; `POST /BulkApply/ResetTemplate` clears them.
- **Attachment behavior**: the user's saved CV is attached to every recipient by default.
- **Response**: re-renders `/BulkApply` with one `BulkApplySendResult` per recipient. SMTP failures for one recipient do not hide successful sends to other recipients.

## Cross-cutting

- **Anti-forgery**: explicitly applied to all POSTs in `JobController`, `ProfileController`, and `BulkApplyController`. Identity Razor Pages enable it by default.
- **Cookies**: ASP.NET Core Identity cookie auth. Login/Logout/AccessDenied paths configured in [Program.cs](../Program.cs).
- **Content-Type**: form-urlencoded for most POSTs; multipart for `POST /Profile`.

---

Last reviewed: 2026-06-01

# Technical Debt

Findings are organized by severity (**High** / **Medium** / **Low**), each with a concrete file:line citation and a suggested improvement. Severity reflects production risk, not refactor effort.

## Phase 1 Closures

The following items from earlier reviews were addressed during Phase 1 (multi-tenant identity foundation):

| ID | Closure |
| --- | --- |
| H1 | Leaked Gemini API key removed from [appsettings.json](../appsettings.json); slot is now empty. Real keys must come from User Secrets / env vars. **Note**: rotating the originally committed key in Google Cloud Console is still the user's responsibility. |
| M6 | `Mscc.GenerativeAI` package removed from [JobApplicationBot.csproj](../JobApplicationBot.csproj). |
| M8 | "Egypt" is still hardcoded; **not closed**. Tracked here. |
| L3 | `HomeController` now serves a real purpose (auth-aware redirect). |
| L4 | `ProfileController` was extracted; `JobController` still owns Analyze/Send/Search. Acceptable for Phase 1. |

## High

### H1. API key committed in source control

- **Where**: [appsettings.json](../appsettings.json) line 11
- **Issue**: `Ai.ApiKey` contains a real-looking Google API key (`AIza...`). Anyone with read access to the repo has full access to that Gemini quota and any GCP services bound to the key.
- **Fix**:
  1. Treat the key as compromised and **rotate** it in Google Cloud Console.
  2. Move secrets to User Secrets for development (`dotnet user-secrets set "Ai:ApiKey" "..."`) and to environment variables / Azure Key Vault / etc. in production.
  3. Add `appsettings.json` placeholder values; never commit real keys.
  4. Add a pre-commit hook (e.g., `gitleaks`) to block accidental key leakage.

### H2. Configured Gemini model name is invalid

- **Where**: [appsettings.json](../appsettings.json) line 12
- **Issue**: `"Model": "gemini-3-flash-preview"`. As of writing there is no Google-published Gemini model with that exact identifier. Calls will fail at runtime with `"Gemini API error: 404 - ..."`. Confidence: **high**.
- **Fix**: Change to a known-valid model id, e.g. `gemini-1.5-flash`, `gemini-1.5-pro`, or `gemini-2.0-flash` (verify against current Google docs). Also consider validating the model on startup (HEAD request) and failing fast.

### H3. LinkedIn scraping violates LinkedIn Terms of Service

- **Where**: [Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 47-126 and 128-183
- **Issue**: Both the `jobs-guest` API call and the DuckDuckGo-mediated post search consume LinkedIn content programmatically. LinkedIn's User Agreement and `robots.txt` forbid automated scraping. Risks: IP bans, legal exposure, brittle integration when LinkedIn changes selectors.
- **Fix**: Either (a) replace with the LinkedIn Talent Solutions API (paid, requires partnership), (b) integrate a sanctioned aggregator (e.g., Adzuna, Indeed Publisher API), or (c) clearly document and accept the risk in a `LICENSE`/`README` disclaimer for personal-use-only.

## Medium

### M1. Silent failure in scraper paths

- **Where**: [Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 81-88 and 144-148
- **Issue**: Both `FetchLinkedInJobs` and `FetchLinkedInPosts` use `catch { return results; }` (empty catches with no logging). A 429, network blip, or CSS selector change all become "0 results" with no operator visibility.
- **Fix**:
  1. Inject `ILogger<JobScraperService>` and log exceptions with the URL, status code, and a snippet of the response.
  2. Distinguish "got HTML, no cards parsed" (likely selector drift) from "HTTP failure" in the logs.
  3. Optionally surface a soft warning in the view, e.g., `"LinkedIn returned no results — your filters may be too narrow, or the integration may be degraded."`

### M2. Raw exception messages reach the UI

- **Where**: [Controllers/JobController.cs](../Controllers/JobController.cs) lines 67-71, 90-94, 139-141
- **Issue**: `ModelState.AddModelError("", $"Error analyzing job: {ex.Message}")` propagates the underlying exception text — possibly including stack-frame fragments, file paths, or HTTP response bodies that contain auth headers (the Gemini error includes `_settings.ApiKey`-bearing URLs).
- **Fix**:
  1. Show a generic message to the user (`"We couldn't analyze that job. Please try again or paste the description manually."`).
  2. Log the full exception via `ILogger`.
  3. Include a correlation ID in the user message so support can trace it to the log entry.

### M3. No structured logging anywhere

- **Where**: Entire project; no `ILogger<T>` is injected into any class.
- **Issue**: The default exception handler is the only diagnostic path. There is no record of which jobs were analyzed, which emails were sent, how long Gemini calls take, etc.
- **Fix**: Inject `ILogger<JobController>`, `ILogger<GeminiAiService>`, `ILogger<JobScraperService>`, `ILogger<EmailSenderService>` and log: requests received, external call latency, parse failures, sent emails (`To` only — never `Body`).

### M4. `UseAuthorization()` with no auth scheme

- **Where**: [Program.cs](../Program.cs) line 35
- **Issue**: The middleware is registered, but no `AddAuthentication`/`AddAuthorization` services and no `[Authorize]` attributes exist. The middleware is effectively a no-op and misleads readers into thinking auth is configured.
- **Fix**: Either remove the line, or — if multi-user support is on the roadmap — add ASP.NET Core Identity / cookie auth and protect `/Job/Send` at minimum (sending email on behalf of someone is a sensitive operation).

### M5. ~~Hidden form fields round-trip server-side state~~ **Closed (CV path)**

- **Where**: [Views/Job/Preview.cshtml](../Views/Job/Preview.cshtml)
- **Resolution**: `CvPath` was removed from `EmailPreviewModel` and the form. CV bytes are now sourced **only** from the database (`UserCvFiles.Content`) inside `JobController.Send`. `JobTitle`, `CompanyName`, and `MatchedSkills[]` are still hidden form fields (display-only, low risk); P1.10 below tracks deferring those into a server-issued token if/when needed.

### M6. `Mscc.GenerativeAI` package declared but unused

- **Where**: [JobApplicationBot.csproj](../JobApplicationBot.csproj) line 13
- **Issue**: The package is referenced but `GeminiAiService` builds raw `HttpClient` calls. This is dead code surface — license, supply-chain risk, and confusion.
- **Fix**: Either (a) remove the package and continue with raw `HttpClient`, or (b) rewrite `GeminiAiService` to use the SDK and gain typed responses + retry logic for free. Option (a) is the smaller change.

### M7. ~~CV path fallback obscures missing-file failures~~ **Closed**

- **Resolution**: `EmailSenderService` no longer reads CVs from disk. It accepts an in-memory `EmailAttachment(FileName, ContentType, Content)` and attaches the bytes directly. The "configured-but-missing" failure mode no longer exists — if the user has no `UserCvFiles` row, the controller simply sends the email without any attachment, and the Preview view warns the user about the missing CV before they click Send.

### M8. Hardcoded "Egypt" location

- **Where**: [Services/JobScraperService.cs](../Services/JobScraperService.cs) lines 15, 131, 175
- **Issue**: The default location is compiled in for both LinkedIn and DuckDuckGo. Reusing this app from another country requires recompilation.
- **Fix**: Move to `JobSearchFilter.Location` (with a default) or a configurable `JobSearch:DefaultLocation`. The Search view already has a `<select>` for experience level — add one for location.

## Low

### L1. `JobSearchViewModel.PagedResults` recomputes on every read

- **Where**: [Models/JobSearchViewModel.cs](../Models/JobSearchViewModel.cs) lines 15-16
- **Issue**: `PagedResults => Results.Skip(...).Take(...).ToList()` runs `Skip/Take` and allocates a new list on each property access. The Razor view reads it twice (count and `foreach`).
- **Fix**: Convert to a method `GetPagedResults()` or memoize via a backing field. Marginal in practice (10 items per page).

### L2. `EmailSenderService` lifetime over-scoped

- **Where**: [Program.cs](../Program.cs) line 22
- **Issue**: Registered as `Scoped` but holds no per-request state — only an `IOptions<EmailSettings>` snapshot.
- **Fix**: Change to `Singleton`. Trivial impact, but consistent with how stateless services should be lifecycled.

### L3. `HomeController` is dead-code redirect

- **Where**: [Controllers/HomeController.cs](../Controllers/HomeController.cs)
- **Issue**: Single action that redirects to `Job/Index`. The default route already maps `/` to `Job/Index`. The only way to reach `HomeController.Index` is the explicit `/Home` URL, which is also handled by the default route convention.
- **Fix**: Delete `HomeController.cs` and `Views/Home/`. The `/Home/Error` reference in the production exception handler ([Program.cs](../Program.cs) line 28) needs a corresponding `Error` action — either move that to `JobController` (and update `UseExceptionHandler("/Job/Error")`), or keep `HomeController` and add a real `Error` action. Currently `Views/Shared/Error.cshtml` exists but is not bound to any controller action.

### L4. `JobController` is a "God controller"

- **Where**: [Controllers/JobController.cs](../Controllers/JobController.cs)
- **Issue**: One controller owns all business actions: input, analyze, preview, send, success, search. At ~150 lines this is fine today, but mixes three domains (analysis, sending, search).
- **Fix**: When more features are added, split into `AnalysisController` / `SearchController` / `EmailController`. Not urgent.

### L5. `CleanJsonResponse` is fragile

- **Where**: [Services/GeminiService.cs](../Services/GeminiService.cs) lines 217-227
- **Issue**: Hand-rolled string trimming for ` ```json ` and ` ``` ` fences. If the model wraps the JSON in any other prelude ("Sure, here's the JSON: { ... }"), parsing fails and a stub object is returned silently.
- **Fix**: Use a regex to extract the first balanced `{...}` block, or set Gemini's `responseMimeType` to `application/json` (Gemini supports this), which removes the need for cleanup.

### L6. No retry / timeout policy on `HttpClient`

- **Where**: [Program.cs](../Program.cs) lines 20-21
- **Issue**: Plain `AddHttpClient<...>()` with no `Polly` policies and the default 100-second timeout. A slow Gemini call will hang the request thread for up to 100 s.
- **Fix**: Add `AddStandardResilienceHandler()` (Microsoft.Extensions.Http.Resilience) or explicit `Polly` retry + timeout (e.g., 30 s timeout, 2 retries with exponential backoff for 5xx).

### L7. Magic strings for LinkedIn filter values

- **Where**: [Views/Job/Search.cshtml](../Views/Job/Search.cshtml) lines 47-52, 62-65
- **Issue**: Experience-level codes (`1`-`6`) and date codes (`r86400`, ...) are hard-coded in the view. Changing them requires editing Razor.
- **Fix**: Move to a static enum or a config-driven dropdown source, exposed by the controller as `ViewBag` / view-model property.

### L8. No favicon caching headers / version pin

- **Where**: [wwwroot/favicon.ico](../wwwroot/favicon.ico)
- **Issue**: Default static-file middleware. Minor.
- **Fix**: Optional — set a long `max-age` for static assets via `StaticFileOptions`.

## Summary Table

| ID | Severity | Area | Fix complexity |
| --- | --- | --- | --- |
| H1 | ~~High~~ Closed | Secrets | Phase 1 removed leaked key from config |
| H2 | High | AI config | Trivial |
| H3 | High | Legal / Integration | High (architectural) |
| M1 | Medium | Observability | Low |
| M2 | Medium | Security / UX | Low |
| M3 | Medium | Observability | Low (cross-cutting) |
| M4 | Medium | Architecture | Replaced by P1 (Identity now in place) |
| M5 | ~~Medium~~ Closed | Security | CV path no longer round-tripped (see P1.9) |
| M6 | ~~Medium~~ Closed | Dependencies | Phase 1 removed unused package |
| M7 | ~~Medium~~ Closed | UX correctness | EmailSenderService now uses in-memory attachments |
| M8 | Medium | Configurability | Low |
| L1 | Low | Performance | Trivial |
| L2 | Low | DI hygiene | Trivial |
| L3 | ~~Low~~ Closed | Dead code | HomeController now used |
| L4 | Low | Code structure | Partial — ProfileController extracted |
| L5 | Low | AI integration | Low |
| L6 | Low | Resilience | Low |
| L7 | Low | Maintainability | Low |
| L8 | Low | Caching | Trivial |
| P1.1 | High | Scalability | High (Phase 3) |
| P1.2 | Medium | Storage | Medium (Phase 3) — moved from disk to SQL Server `varbinary(max)` |
| P1.3 | High | Email correctness | High (Phase 2) |
| P1.4 | Medium | Anti-spam | Low (after Phase 2) |
| P1.5 | Medium | Crypto state | Low (Phase 3) |
| P1.6 | Medium | Quota correctness | Low |
| P1.7 | Low | Roles | Low |
| P1.8 | Low | UI polish | Low |
| P1.9 | ~~Medium~~ Closed | Security | `CvPath` removed from form/model |

## New Items Surfaced by Phase 1

### P1.1 (High) Single-instance state will not survive horizontal scaling

- **Where**: [Program.cs](../Program.cs) (`AddMemoryCache`, `AddSession`, `AddDataProtection`)
- **Issue**: Three pieces of state are local to the process: `IMemoryCache` (search results), in-process Session (TempData), and DataProtection key ring (encrypts `PersonalGeminiApiKey` at rest). Running two instances behind a load balancer will: (a) lose search caches across pods, (b) lose TempData on session affinity failover, (c) make the `PersonalGeminiApiKey` column unreadable on the second instance.
- **Fix**: Phase 3 — replace `IMemoryCache` with `AddStackExchangeRedisCache`; replace `AddSession` with distributed session; persist DataProtection keys to a shared store (Azure Key Vault, Redis, or a shared blob).

### P1.2 (Medium) CV files are stored as `varbinary(max)` in SQL Server

- **Where**: [Data/Entities/UserCvFile.cs](../Data/Entities/UserCvFile.cs), `UserCvFiles.Content` column
- **Status**: Phase 1 update closed the local-disk problem (which broke multi-instance deployments). CVs are now in the database, so any pod can read them. Confidence: **high**.
- **Remaining issue**: SQL Server `varbinary(max)` is fine at thousands of users but DB backup size grows linearly with `user_count × max_cv_size`. At millions of users this becomes the dominant backup cost. Also: no virus scanning, no per-user storage quotas, no lifecycle policy.
- **Fix (Phase 3)**: Move CV bytes to Azure Blob Storage (or S3); keep only metadata (`FileName`, `ContentType`, `SizeBytes`, blob URI / SAS) in `UserCvFiles`. Add a virus-scan hook (Defender for Storage / ClamAV).

### P1.3 (Medium) Job preview send still uses shared SenderEmail

- **Where**: [Services/EmailSenderService.cs](../Services/EmailSenderService.cs)
- **Status**: Bulk apply now sends through per-user Gmail SMTP App Password credentials stored in `UserEmailCredentials`. The older job-analysis `POST /Job/Send` path still uses the app-level `EmailSettings` account.
- **Remaining issue**: Users must paste a Gmail App Password. This works, but Gmail OAuth is a better long-term UX/security model because users click "Connect Google" and can revoke access without managing a pasted secret.
- **Fix**: Phase 2 — move all application sending paths (`JobController.Send` and `BulkApplyController.Send`) behind a provider-aware user email sender that supports Gmail OAuth first and SMTP fallback second.

### P1.4 (Medium) Email confirmation disabled

- **Where**: [Program.cs](../Program.cs) — `RequireConfirmedAccount = false`
- **Issue**: Anyone can register with any email. There is no anti-spam barrier and no recovery if a user mistypes their email.
- **Fix**: Add an SMTP-based `IEmailSender` for Identity (separate from the application email service), enable confirmation. Couple with rate-limiting on the registration endpoint.

### P1.5 (Medium) DataProtection key store is filesystem default

- **Where**: [Program.cs](../Program.cs) — `AddDataProtection()`
- **Issue**: Default key persistence is `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`. After a host rebuild, all stored `PersonalGeminiApiKey` values become unrecoverable. Also blocks horizontal scale (see P1.1).
- **Fix**: Persist keys to Key Vault / Redis / a stable blob path. Override the application name to keep key isolation across deployments.

### P1.6 (Medium) Quota enforcement is timing-of-check vs timing-of-use

- **Where**: [Controllers/JobController.cs](../Controllers/JobController.cs) `Analyze`
- **Issue**: The counter is incremented **before** the Gemini call. If Gemini fails (network, 429, parse error), the user's quota was consumed even though they got no useful output.
- **Fix**: Either decrement on failure (best-effort) or restructure to: call Gemini first → on success → increment. The latter has a small over-quota window under concurrency but is more user-friendly. Pick one and document it.

### P1.7 (Low) `[Authorize(Roles=...)]` is unused but the schema supports it

- **Where**: Roles tables exist via Identity but no roles are seeded. No `IsAdmin`/admin UI exists.
- **Fix**: When admin features arrive (e.g., view all users' usage), add a minimal seeding step for an `Admin` role and protect a future `AdminController` with `[Authorize(Roles = "Admin")]`.

### P1.8 (Low) Identity UI uses default scaffolded styling

- **Where**: [Areas/Identity/Pages/_ViewStart.cshtml](../Areas/Identity/Pages/_ViewStart.cshtml) overrides only the layout
- **Issue**: Forms inside Login/Register inherit minimal styling. Looks acceptable but is not gradient-themed like the rest of the app.
- **Fix**: When desired, run `dotnet aspnet-codegenerator identity` to scaffold the pages and restyle them.

### P1.9 ~~(Medium) Unsafe `cvPath` form round-trip~~ **Closed**

- **Resolution**: The "Store CV in Database" change (June 2026) removed `CvPath` from `EmailPreviewModel` and from `Views/Job/Preview.cshtml`'s hidden inputs. CVs are sourced exclusively from `UserCvFiles.Content` keyed by the authenticated user, so the form can no longer influence which file is attached.

## Suggested Order of Work

1. **H2** (set a valid Gemini model name) is still required before the app can run end-to-end.
2. **P1.6** (quota TOCTOU) — small fix, reduces user frustration.
3. **M3, M1, M2** — add `ILogger`, replace silent catches, sanitize error messages.
4. **P1.4** (email confirmation) — once Phase 2's email infrastructure is in place.
5. **Phase 2**: H3 (LinkedIn ToS), P1.3 (per-user email).
6. **Phase 3**: P1.1, P1.2 (CV → blob storage), P1.5 (scale-out — Redis, Blob, distributed key ring).

---

Last reviewed: 2026-06-01

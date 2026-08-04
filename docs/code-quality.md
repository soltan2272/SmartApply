# Code Quality Report

> Findings only — **no code was modified**. Severity is the author's assessment for prioritization.

## Table of Contents

- [1. Architecture Strengths](#1-architecture-strengths)
- [2. Security Concerns](#2-security-concerns)
- [3. Code Smells](#3-code-smells)
- [4. Technical Debt](#4-technical-debt)
- [5. Duplicated Logic](#5-duplicated-logic)
- [6. Performance Concerns](#6-performance-concerns)
- [7. Correctness / Bugs](#7-correctness--bugs)
- [8. Refactoring Opportunities](#8-refactoring-opportunities)
- [9. Best-Practice Recommendations](#9-best-practice-recommendations)
- [10. Priority Summary](#10-priority-summary)

---

## 1. Architecture Strengths

- **Clean, consistent layering** (controllers → services → repositories → EF) with interface-based seams and constructor DI throughout.
- **Secrets encrypted at rest** via `EncryptedStringConverter` + DataProtection for the personal Gemini key and Gmail App Password.
- **Thoughtful data access:** `AsNoTracking()` reads, a metadata projection to avoid loading the CV blob, and an **atomic, retry-based quota counter** backed by a unique index.
- **Fail-soft UX:** external failures (AI, scrape, SMTP) surface as friendly model errors or per-recipient results rather than 500s.
- **Pluggable LLM backend** (Gemini vs OpenAI-compatible) selected via config.
- **Request size hardening** (6 MB cap at Kestrel + FormOptions) and CV upload constraints.
- **Cancellation tokens** threaded through most async paths.

## 2. Security Concerns

| # | Severity | Finding | Location |
|---|---|---|---|
| S1 | **High** | **Live secrets committed to repo:** `Ai:ApiKey` in `appsettings.json`; real Gmail App Password in `appsettings.Development.json`. | `appsettings*.json` |
| S2 | **High** | **SSRF:** server fetches arbitrary user-supplied `JobUrl` with no host allowlist / private-IP block. | `JobController.Analyze` → `JobScraperService.ScrapeJobDescriptionAsync` |
| S3 | Medium | **No account lockout** (`lockoutOnFailure: false`) — brute-force friendly. | `LoginModel` |
| S4 | Medium | **DataProtection keys not explicitly persisted/shared** — encrypted columns may become undecryptable after key-ring reset or across scaled instances. | `Program.cs` |
| S5 | Medium | **No rate limiting** on email sends (single + bulk up to 50). | `Job/BulkApply` controllers |
| S6 | Low | If the `DbContext` is created without `IDataProtectionProvider` (e.g. some design-time paths), secret columns are written as plaintext. | `ApplicationDbContext.OnModelCreating` |

## 3. Code Smells

- **Fat controllers:** `BulkApplyController` (validation, template building, send loop) and `ProfileController` (upload + 3 upserts) hold business logic that belongs in a service. Contradicts "keep controllers thin".
- **Broad `catch (Exception)`** blocks that surface `ex.Message` directly to the UI (`Job.Analyze/Send/Search`) — can leak internal details and loses stack traces (not logged).
- **Silent catch-all** in `JobScraperService` (`catch { return results; }`) hides root causes; no logging.
- **Stringly-typed TempData keys** (`"ProfileIncomplete"`, `"Success"`, `"RecipientEmail"`, ...) scattered across controllers/pages.
- **Magic numbers** partially centralized (e.g. `MaxRecipients`, `MaxCvSizeBytes`) but 15-min cache TTL, page size 10, and SMTP defaults are inline.

## 4. Technical Debt

- **No automated tests** (no test project). All logic is unverified by CI.
- **No transaction** wrapping the multi-step `ProfileController.Post` (CV + profile + credential) — partial persistence possible on failure.
- **`/Home/Error` route has no `Error` action** on `HomeController` (production `UseExceptionHandler("/Home/Error")`). Error rendering relies on view resolution and may not behave as intended.
- **Migrations not auto-applied**; onboarding requires manual `dotnet ef database update` (not documented in-repo outside these docs).
- **Build artifacts committed** (`bin/`, `obj/`, `.vs/` appear in git status) — should be git-ignored.
- **No `UserSecretsId`** despite storing dev secrets — pushes secrets into appsettings.

## 5. Duplicated Logic

- **Global-account construction** duplicated in `EmailSenderService.SendEmailAsync` and `SendHtmlEmailAsync` (identical credential check + `EmailSenderAccount` build).
- **`EmailSenderAccount` vs `EmailSettings`** overlap (same fields conceptually).
- **`CurrentUserId` helper** re-implemented in `JobController`, `BulkApplyController`, `ProfileController` (could be a base controller or extension).
- **Upsert "update only if non-null, empty→null" secret pattern** repeated in `UserProfileRepository` and `UserEmailCredentialRepository`.
- **Profile-completeness / "has credential" checks** computed in multiple places (`BulkApplyController`, `ProfileController`, `JobController.GetOrPromptProfileAsync`).

## 6. Performance Concerns

- **CV stored as `varbinary(max)` in the primary DB** and loaded fully into memory for download/send; fine at 5 MB scale but not blob-storage-optimal.
- **Bulk send is synchronous and serial**, one SMTP connect/auth per recipient inside the HTTP request → slow for large batches, ties up a request thread, and risks timeouts. Should be a background job/queue.
- **In-memory search cache** is per-instance; won't scale horizontally and duplicates work across nodes.
- **Scraping on the request thread** (LinkedIn/DuckDuckGo/arbitrary URL) adds latency and external-dependency risk to user requests.

## 7. Correctness / Bugs

- **Quota consumed before AI success:** `TryConsumeAsync` increments before the Gemini call; a subsequent failure still counts against the user's quota (no compensation/refund).
- **Concurrency token missing:** `AiUsageRepository` update path catches `DbUpdateConcurrencyException`, but `AiUsage` has no `RowVersion`, so that exception won't actually fire on concurrent updates → potential lost increment under contention (the unique-index insert race is handled correctly, though).
- **`GenerateEmailAsync` `MatchedSkills`** does a substring `Contains` on `SkillsSummary` — can produce false positives/negatives (e.g. "Java" matches "JavaScript").
- **`Model` default mismatch:** `AiSettings.Model` default is `gemini-1.5-flash` in code but config uses `gemini-2.5-flash`; harmless but confusing.

## 8. Refactoring Opportunities

1. Extract a `BulkApplyService` and `ProfileService` to thin the controllers.
2. Introduce a `CurrentUser` accessor (base controller or `IUserContext`) to remove duplicated claim lookups.
3. Consolidate global SMTP account creation in `EmailSenderService` into one private factory.
4. Move bulk email sending to a background queue / hosted service with retry + rate limiting.
5. Add an SSRF guard (allowlist / block private ranges / scheme check) before scraping.
6. Add a `RowVersion` to `AiUsage` (or use a DB-side atomic `UPDATE ... WHERE CallCount < limit`) for a race-free counter.
7. Wrap `ProfileController.Post` writes in a single transaction (or a Unit of Work).
8. Centralize constants (cache TTL, page size) in a settings/options class.

## 9. Best-Practice Recommendations

- Add a **test project** (unit tests for `AiQuotaService`, repositories, validation; integration tests for controllers).
- Add **`.gitignore`** for `bin/`, `obj/`, `.vs/`; remove tracked build artifacts.
- Move secrets to **user-secrets/env/Key Vault**; rotate the exposed key + app password.
- Add **structured logging** around external calls and a **global exception logging** middleware; stop echoing raw `ex.Message` to users.
- Configure **DataProtection key persistence** for production.
- Add **rate limiting** (`AddRateLimiter`) for email/AI endpoints.
- Document **DB migration** and **run** steps in a repo `README`.

## 10. Priority Summary

| Priority | Items |
|---|---|
| **P0 (do first)** | S1 rotate/move secrets; S2 SSRF guard |
| **P1** | Quota-before-failure fix; DataProtection keys (S4); transaction for profile save; add `.gitignore` |
| **P2** | Background queue for bulk send; lockout (S3); rate limiting (S5); thin controllers |
| **P3** | Dedup helpers; constants; tests; logging; skill-match accuracy |

---

_See also: [07-security.md](07-security.md), [06-data-access.md](06-data-access.md), [11-background-jobs.md](11-background-jobs.md)._

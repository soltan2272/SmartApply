# AI_CONTEXT — SmartApply Hub

> **Read this file first** in any AI-assisted session. It is the compressed, authoritative model of the project. For depth, follow the links to the numbered docs and `features/`.

## Table of Contents

- [1. Project Overview](#1-project-overview)
- [2. Business Domain](#2-business-domain)
- [3. Architecture](#3-architecture)
- [4. Folder Structure](#4-folder-structure)
- [5. Naming Conventions](#5-naming-conventions)
- [6. Coding Standards](#6-coding-standards)
- [7. Design Patterns](#7-design-patterns)
- [8. Main Entities](#8-main-entities)
- [9. Main Services](#9-main-services)
- [10. Business Terminology](#10-business-terminology)
- [11. Important Workflows](#11-important-workflows)
- [12. Authentication Flow](#12-authentication-flow)
- [13. Database Overview](#13-database-overview)
- [14. API Overview](#14-api-overview)
- [15. Known Limitations](#15-known-limitations)
- [16. Technical Debt](#16-technical-debt)
- [17. Important Implementation Notes](#17-important-implementation-notes)

---

## 1. Project Overview

- **Name:** SmartApply Hub (project/assembly `JobApplicationBot`).
- **What:** AI-powered job-application assistant. Users analyze a job, generate a tailored application email (Google Gemini), and send it with their CV — individually or in bulk. Also a LinkedIn job search.
- **Stack:** ASP.NET Core **9.0** MVC + Razor Pages (Identity), EF Core 9 (SQL Server), MailKit SMTP, HtmlAgilityPack, Bootstrap 5.
- **Shape:** Single-project layered monolith. No microservices, no separate class libraries.
- Details: [01-project-overview.md](01-project-overview.md).

## 2. Business Domain

- Freemium: **20 free AI analyses/month/user** (`Ai:FreeQuotaPerMonth`); bypassed by a personal Gemini key.
- Two send paths: **single Apply** uses the **global** SMTP account; **Bulk Apply** uses the **user's own Gmail** credential.
- No roles, no approvals, no multi-tenant. Data strictly scoped per user id.
- Details: [02-business-domain.md](02-business-domain.md).

## 3. Architecture

`Controllers → Services → Repositories → ApplicationDbContext → SQL Server`. Interface-based DI, constructor injection, options pattern for config. No CQRS, no MediatR, no generic repository, no Unit of Work abstraction (DbContext is the UoW). Details: [12-architecture.md](12-architecture.md).

## 4. Folder Structure

```
Controllers/   Home, Job, BulkApply, Profile
Services/      GeminiService(IAiService), JobScraperService, EmailSenderService,
               IdentityEmailSender, UserAccountSetupService, Quota/AiQuotaService
Data/          ApplicationDbContext, EncryptedStringConverter,
               Entities/*, Repositories/*
Models/        view models, DTOs, AppSettings(AiSettings,EmailSettings), AppBranding
Areas/Identity/Pages/Account/   Register, Login, ConfirmEmail, Forgot/Reset password...
Views/         Home, Job, BulkApply, Profile, Shared
Migrations/    3 migrations + snapshot
```

## 5. Naming Conventions

- Interfaces prefixed `I` (`IAiService`, `IUserProfileRepository`).
- One repository per aggregate: `{Entity}Repository` + `I{Entity}Repository`.
- View/request models suffixed `ViewModel` or descriptive (`JobInput`, `EmailPreviewModel`).
- Options classes suffixed `Settings` (`AiSettings`, `EmailSettings`).
- Small immutable value types are `record`s (`EmailAttachment`, `QuotaCheckResult`, `CvFileMetadata`, `EmailSenderAccount`).
- Async methods suffixed `Async` and take `CancellationToken ct`.
- Namespaces mirror folders under `JobApplicationBot.*`.
- Tables = pluralized DbSet names (`UserProfiles`, `AiUsages`).

## 6. Coding Standards

- Nullable reference types **enabled**; implicit usings **enabled**.
- File-scoped namespaces.
- UTC timestamps everywhere (`DateTime.UtcNow`).
- Reads: `AsNoTracking()`. Writes: load-then-mutate upsert + `SaveChangesAsync`.
- Secrets go through `EncryptedStringConverter` (never plaintext).
- POST actions use `[ValidateAntiForgeryToken]`; feature controllers use `[Authorize]`.
- Current user = `User.FindFirstValue(ClaimTypes.NameIdentifier)`.

## 7. Design Patterns

MVC, Razor Pages, Repository, DI, Options, Adapter (`IdentityEmailSender`), Strategy (LLM endpoint switch), Value Converter (encryption), PRG. See [12-architecture.md](12-architecture.md#2-design-patterns-in-use).

## 8. Main Entities

| Entity | Table | Key | Notes |
|---|---|---|---|
| `ApplicationUser` | `AspNetUsers` | `Id` | Identity user + `Profile` nav |
| `UserProfile` | `UserProfiles` | `UserId` (PK+FK) | candidate data + encrypted `PersonalGeminiApiKey` |
| `UserCvFile` | `UserCvFiles` | `UserId` (PK+FK) | CV bytes `varbinary(max)` |
| `UserEmailCredential` | `UserEmailCredentials` | `UserId` (PK+FK) | Gmail sender + encrypted `Secret` |
| `AiUsage` | `AiUsages` | `Id` | per user/month counter, unique `(UserId,Year,Month)` |

Full schema + ER diagram: [03-database.md](03-database.md).

## 9. Main Services

| Service | Role |
|---|---|
| `IAiService`/`GeminiAiService` | Gemini job analysis + email generation (also OpenAI-compatible) |
| `IJobScraperService`/`JobScraperService` | URL scrape + LinkedIn/DuckDuckGo search |
| `IEmailSenderService`/`EmailSenderService` | SMTP send (global or per-user account) |
| `IdentityEmailSender` | Identity confirm/reset emails |
| `IUserAccountSetupService` | ensure profile on register/login |
| `IAiQuotaService`/`AiQuotaService` | freemium quota (+ `AiUsageRepository` atomic counter) |
| `IEmailExtractionService`/`EmailExtractionService` | Extract unique emails from pasted free-form text (Bulk Apply) |
| `ICvTextExtractionService`/`CvTextExtractionService` | Extract plain text from PDF/DOCX CVs |
| `IApplicationTrackingService`/`ApplicationTrackingService` | Track applications + enqueue bulk sends + follow-ups |
| `BulkEmailDispatchWorker` | Background hosted service that processes bulk email queues |

Full detail: [05-services.md](05-services.md).

## 10. Business Terminology

- **Profile:** candidate's reusable data (name, title, skills, experience, contact email).
- **Personal Gemini API Key:** user-supplied LLM key that bypasses the free quota.
- **Gmail App Password / Credential (`Secret`):** encrypted SMTP secret used for Bulk Apply.
- **Quota / AiUsage:** monthly free AI-analysis counter.
- **Analyze:** LLM extraction of structured job data. **Generate:** LLM drafting of the email.
- **Single Apply vs Bulk Apply:** one recipient via global SMTP vs many via the user's Gmail.

## 11. Important Workflows

Analyze, Send, Bulk Apply, Job Search, Save Profile, Register/Confirm, Login (first-run prompt). Sequence diagrams: [09-business-flows.md](09-business-flows.md).

## 12. Authentication Flow

Cookie-based ASP.NET Core Identity; **email confirmation required** before login; optional Google OAuth. On register/login → `UserAccountSetupService.EnsureProfileAsync`; on login without a saved Gmail App Password → redirect to Profile. No JWT, no roles, no policies. See [07-security.md](07-security.md).

## 13. Database Overview

SQL Server (LocalDB default `JobApplicationBot`). Identity tables + 4 app tables. 1:1 tables use `UserId` as PK+FK with cascade delete. Two encrypted columns. No soft delete, no auditing infra (manual timestamps). Migrations applied manually (`dotnet ef database update`). See [03-database.md](03-database.md).

## 14. API Overview

Server-rendered MVC/Razor (returns views/redirects, **not JSON**). Feature controllers `[Authorize]`. Key endpoints: `/Job` (Index/Analyze/Send/Success/Search), `/BulkApply` (Index/Send), `/Profile` (Index GET/POST, Cv), Identity `/Identity/Account/*`. Full list: [04-api-reference.md](04-api-reference.md).

## 15. Known Limitations

- Bulk send is synchronous/serial in-request (no queue).
- Job search scraping is fragile and Egypt-only; failures return empty silently.
- No tests, no rate limiting, no account lockout.
- In-memory cache and DataProtection keys are per-instance (not scale-out ready).

## 16. Technical Debt

- Secrets committed in `appsettings*.json` (rotate + move out).
- SSRF via arbitrary URL scraping.
- Quota consumed even when the AI call fails.
- Multi-step profile save not transactional.
- `/Home/Error` has no matching action.
- Fat controllers (Bulk/Profile). Full report: [code-quality.md](code-quality.md).

## 17. Important Implementation Notes

- **Do not** add business logic to controllers — put it in `Services/` (see [.cursor/rules.md](../.cursor/rules.md)).
- **Do not** invent skills/experience — when generating emails, prefer CV text + profile fields; `GenerateEmailAsync` accepts optional `cvText`.
- **Reuse** `IEmailSenderService`, `IAiService`, `IAiQuotaService`, `ICvTextExtractionService`, and the existing repositories; don't create parallel implementations.
- **Two SMTP paths exist** — global vs per-user; pick the correct overload of `SendEmailAsync`.
- **Persist secrets only through `EncryptedStringConverter`**-mapped columns.
- **Thread `CancellationToken`** and use `AsNoTracking()` for reads.
- **LLM backend is config-driven** (`Ai:BaseUrl`); keep the Gemini/OpenAI branch behavior intact.
- **Quota is per calendar month UTC**; rely on the `(Year,Month)` row + unique index.
- When adding features, **update the matching docs** and this file.

---

_Index of all docs: [README.md](README.md)._

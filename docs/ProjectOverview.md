# Project Overview

## Purpose

`JobApplicationBot` is a multi-tenant ASP.NET Core MVC web application that automates the early stages of applying for a job. After signing up, each user configures their profile, uploads a CV, and uses AI to generate tailored application emails for any job posting.

User-facing flow per signed-in user:

1. **Discover** job postings (LinkedIn job listings + LinkedIn "hiring" posts surfaced via DuckDuckGo).
2. **Analyze** a job description with Google Gemini, which extracts title, company, contact email, required skills, responsibilities, experience level, and a summary.
3. **Generate** a tailored application email body and subject using the user's profile.
4. **Send** the email through Gmail SMTP with the user's CV attached.

## Technologies Used

| Layer | Technology | Source |
| --- | --- | --- |
| Runtime | .NET 9.0 | [JobApplicationBot.csproj](../JobApplicationBot.csproj) |
| Web framework | ASP.NET Core MVC + Razor Views + Razor Pages (Identity UI) | [Program.cs](../Program.cs) |
| ORM | Entity Framework Core 9 | [JobApplicationBot.csproj](../JobApplicationBot.csproj) |
| Database | SQL Server (LocalDB in dev; Azure SQL or any SQL Server in prod) | `ConnectionStrings:DefaultConnection` in [appsettings.json](../appsettings.json) |
| Identity | ASP.NET Core Identity (cookie-based) + Google external login | [Program.cs](../Program.cs) |
| Secret encryption | `Microsoft.AspNetCore.DataProtection` (encrypts per-user Gemini API keys) | [Data/EncryptedStringConverter.cs](../Data/EncryptedStringConverter.cs) |
| HTML parsing | HtmlAgilityPack 1.12.4 | [Services/JobScraperService.cs](../Services/JobScraperService.cs) |
| SMTP / MIME | MailKit 4.15.0, MimeKit 4.15.0 | [Services/EmailSenderService.cs](../Services/EmailSenderService.cs) |
| AI | Google Gemini REST API (with OpenAI-compatible fallback path) | [Services/GeminiService.cs](../Services/GeminiService.cs) |
| Caching | `IMemoryCache` (per-process; will move to Redis in Phase 3) | [Program.cs](../Program.cs) |

## Architecture Pattern

ASP.NET Core MVC + **Service Layer** + **Repository Layer** + **EF Core / SQL Server**.

```
Browser
   |
   v
Controllers (under [Authorize])
   |
   v
Service Layer (Quota, AI, Scraper, Email)
   |
   v
Repository Layer (UserProfile, AiUsage)
   |
   v
ApplicationDbContext (EF Core)
   |
   v
SQL Server
```

External-system calls (Gemini, LinkedIn, DuckDuckGo, Gmail SMTP) sit at the service layer and bypass the repository tier; nothing about an external response is persisted.

## Solution Structure

The solution [JobApplicationBot.sln](../JobApplicationBot.sln) contains exactly **one** project, [JobApplicationBot.csproj](../JobApplicationBot.csproj). Top-level layout:

```
JobApplicationBot/
├── Areas/Identity/        Razor Pages override (re-uses _Layout.cshtml)
├── Controllers/           MVC controllers (Job, Home, Profile)
├── Data/                  ApplicationDbContext, entities, repositories, encrypted converter
│   ├── Entities/
│   └── Repositories/
├── Migrations/            EF Core migrations
├── Services/              Service implementations + interfaces
│   └── Quota/             AI quota service
├── Models/                DTOs, view models, settings POCOs
├── Views/                 Razor views (Job, Home, Profile, Shared)
├── wwwroot/               Static assets
├── Properties/            launchSettings.json
├── Program.cs             Composition root
├── appsettings.json       Runtime configuration
└── JobApplicationBot.csproj
```

## Main Business Modules

The codebase exposes six logical modules. Each is documented in [BusinessModules.md](BusinessModules.md).

| # | Module | Primary service | Primary view |
| --- | --- | --- | --- |
| 1 | Identity & Profile | `UserManager`, `IUserProfileRepository`, `IAiQuotaService` | `Views/Profile/Index.cshtml`, Identity Razor Pages |
| 2 | Job Discovery | `JobScraperService` | `Views/Job/Search.cshtml` |
| 3 | Job Analysis | `GeminiAiService.AnalyzeJobAsync` | (intermediate; feeds module 4) |
| 4 | Email Generation | `GeminiAiService.GenerateEmailAsync` | `Views/Job/Preview.cshtml` |
| 5 | Email Delivery | `EmailSenderService` | `Views/Job/Success.cshtml` |
| 6 | Bulk CV Apply | `BulkApplyController`, `EmailSenderService` | `Views/BulkApply/Index.cshtml` |

## Multi-Tenancy & Cost Model

- **Tenancy**: per-user; isolated via `ApplicationUser.Id` foreign keys on every per-user table. Confidence: high.
- **Identity**: ASP.NET Core Identity with email + password. Optional Google sign-in via OAuth 2.0 (`Authentication:Google:ClientId/ClientSecret` in [appsettings.json](../appsettings.json) or User Secrets).
- **AI quota**: freemium — each user gets `Ai:FreeQuotaPerMonth` analyses per calendar month using a shared Gemini key. Users can paste their own Gemini API key on the Profile page to bypass the quota; the key is encrypted at rest via `IDataProtectionProvider`.
- **Email sending**: individual job sends can use global SMTP settings; bulk apply uses each user's saved Gmail SMTP App Password from `UserEmailCredentials`, encrypted at rest.

## Setup

After cloning:

1. Install SQL Server LocalDB (ships with Visual Studio) or change `ConnectionStrings:DefaultConnection` to point at any SQL Server instance.
2. Apply the database schema:
   ```
   dotnet ef database update
   ```
3. Set the shared Gemini key via User Secrets (do not commit it):
   ```
   dotnet user-secrets init
   dotnet user-secrets set "Ai:ApiKey" "YOUR_GEMINI_KEY"
   ```
4. (Optional) For Google sign-in, create a Google Cloud OAuth 2.0 Client ID (Web application, redirect URI `https://localhost:PORT/signin-google`) and configure:
   ```
   dotnet user-secrets set "Authentication:Google:ClientId" "..."
   dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
   ```
5. (Optional) Configure app-level SMTP for Identity/Forgot Password in `Email:*` settings. Users configure their own Gmail App Password on `/Profile` before using `/BulkApply`.
6. Run with `dotnet run`.

## Documentation Scope

| File | Status | Notes |
| --- | --- | --- |
| [ProjectOverview.md](ProjectOverview.md) | this file | |
| [RepositoryMap.md](RepositoryMap.md) | present | |
| [DatabaseStructure.md](DatabaseStructure.md) | **present** | Created in Phase 1 (database introduced). |
| [APIInventory.md](APIInventory.md) | present | All Job actions are now `[Authorize]`. |
| [BusinessModules.md](BusinessModules.md) | present | Five modules. |
| [ServiceFlow.md](ServiceFlow.md) | present | Repository layer documented. |
| [ExternalIntegrations.md](ExternalIntegrations.md) | present | Google OAuth + SQL Server added. |
| [ArchitectureDiagrams.md](ArchitectureDiagrams.md) | present | ER diagram replaces in-memory data-object diagram. |
| [BusinessRules.md](BusinessRules.md) | present | Includes quota and authorization rules. |
| [TechnicalDebt.md](TechnicalDebt.md) | present | Phase 1 closed several items; new items added. |

## Phase Roadmap

- **Phase 1 (this release)**: identity, per-user profile, freemium quota, SQL Server. Done.
- **Phase 2**: per-user email — Gmail OAuth 2.0 (default) + BYO SMTP fallback for non-Gmail users. Bulk apply currently supports per-user Gmail SMTP App Passwords and is shaped to migrate to OAuth later.
- **Phase 3**: scale-out — Redis distributed cache (replaces `IMemoryCache`), Azure Blob Storage / S3 for CVs (replaces `UserCvFiles.Content` in SQL Server), Docker, deployment pipelines, observability.

---

Last reviewed: 2026-06-01

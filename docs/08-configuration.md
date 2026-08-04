# 08 – Configuration

> Files: `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`. Options classes: `Models/AppSettings.cs`.

## Table of Contents

- [1. Configuration Sources](#1-configuration-sources)
- [2. appsettings.json](#2-appsettingsjson)
- [3. appsettings.Development.json](#3-appsettingsdevelopmentjson)
- [4. Options Classes](#4-options-classes)
- [5. Connection Strings](#5-connection-strings)
- [6. AI Settings](#6-ai-settings)
- [7. SMTP / Email](#7-smtp--email)
- [8. Google Authentication](#8-google-authentication)
- [9. Logging](#9-logging)
- [10. Environment Variables & Launch Profiles](#10-environment-variables--launch-profiles)
- [11. Not Configured / Not Present](#11-not-configured--not-present)
- [12. Secrets Management](#12-secrets-management)

---

## 1. Configuration Sources

Standard ASP.NET Core configuration order applies (later overrides earlier):
1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. Environment variables
4. User secrets (Development) — supported but no `UserSecretsId` is set in the csproj, so user-secrets require adding one.
5. Command-line args

## 2. appsettings.json

| Section | Key | Value (repo) | Meaning |
|---|---|---|---|
| `Logging:LogLevel` | `Default` | `Information` | Default log level |
| | `Microsoft.AspNetCore` | `Warning` | Framework noise reduced |
| `AllowedHosts` | | `*` | Any host |
| `ConnectionStrings` | `DefaultConnection` | LocalDB `JobApplicationBot` | SQL Server connection |
| `Ai` | `ApiKey` | *(present in repo)* | Shared Gemini key ⚠️ |
| | `Model` | `gemini-2.5-flash` | Model name |
| | `BaseUrl` | `https://generativelanguage.googleapis.com/v1beta` | Endpoint (Google shape when host contains `googleapis.com`) |
| | `FreeQuotaPerMonth` | `20` | Free AI analyses/month |
| `Email` | `SmtpServer` | `smtp.gmail.com` | SMTP host |
| | `SmtpPort` | `587` | STARTTLS port |
| | `SenderEmail` | `smaartapplyhub@gmail.com` | Global sender |
| | `SenderPassword` | `""` (empty here) | Gmail App Password (set in Dev/env) |
| | `SenderName` | `SmartApply Hub` | Display name |
| `Authentication:Google` | `ClientId` | `""` | Google OAuth (disabled when empty) |
| | `ClientSecret` | `""` | Google OAuth |

## 3. appsettings.Development.json

Overrides `Logging` and `Email`. **Contains a real Gmail App Password** (`Email:SenderPassword`) — see [Section 12](#12-secrets-management). Does **not** override `Ai`, `ConnectionStrings`, or `Authentication`.

## 4. Options Classes

`Models/AppSettings.cs`:

```csharp
public class AiSettings {
    public string ApiKey = "";
    public string Model = "gemini-1.5-flash";        // default; overridden to gemini-2.5-flash in config
    public string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public int FreeQuotaPerMonth = 20;
}
public class EmailSettings {
    public string SmtpServer = "smtp.gmail.com";
    public int SmtpPort = 587;
    public string SenderEmail = "";
    public string SenderPassword = "";
    public string SenderName = "";
}
```

Bound in `Program.cs`:
```csharp
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
```
Consumed via `IOptions<AiSettings>` (in `GeminiAiService`, `AiQuotaService`) and `IOptions<EmailSettings>` (in `EmailSenderService`).

## 5. Connection Strings

- **Key:** `ConnectionStrings:DefaultConnection`.
- **Default:** `Server=(localdb)\MSSQLLocalDB;Database=JobApplicationBot;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true`.
- **Required:** `Program.cs` throws `InvalidOperationException` if missing.
- `MultipleActiveResultSets=true` enables MARS.

## 6. AI Settings

- `Ai:ApiKey` is the fallback/shared key; a user's `PersonalGeminiApiKey` overrides it per request.
- `Ai:BaseUrl` selects request format: contains `googleapis.com` → Gemini shape; otherwise → OpenAI-compatible chat shape (Bearer token). This allows pointing at alternative LLM gateways (e.g. GLM) without code changes.
- `Ai:FreeQuotaPerMonth` controls the freemium limit enforced by `AiQuotaService`.

## 7. SMTP / Email

Two distinct email paths:

| Path | Account used | Config source |
|---|---|---|
| Identity emails (confirm/reset), single Apply send | **Global** `Email:*` | `EmailSettings` |
| Bulk Apply send | **Per-user** Gmail | `UserEmailCredentials` row (encrypted `Secret`) |

Startup logs a **warning** if `Email:SenderEmail`/`Email:SenderPassword` are not both set (identity + single-apply email will fail until configured). Gmail requires an **App Password**, not the normal account password.

## 8. Google Authentication

- Keys: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`.
- If either is empty, Google login is **not registered** and the button does not appear.

## 9. Logging

- Provider: default ASP.NET Core console logging.
- Levels from `Logging:LogLevel` in both appsettings files (Default `Information`, `Microsoft.AspNetCore` `Warning`).
- No structured/file/third-party logging configured.

## 10. Environment Variables & Launch Profiles

`Properties/launchSettings.json` profiles (Development):

| Profile | URL(s) |
|---|---|
| `http` | `http://localhost:5210` |
| `https` | `https://localhost:7039;http://localhost:5210` |
| `IIS Express` | IIS ports (`61154`, SSL `44361`) |

All profiles set `ASPNETCORE_ENVIRONMENT=Development`. Any config key can be overridden by environment variables using the `Section__Key` convention (e.g. `Email__SenderPassword`, `Ai__ApiKey`, `ConnectionStrings__DefaultConnection`).

## 11. Not Configured / Not Present

The following are **not used** in this project (explicitly noted so nothing is assumed):

- **Redis** — not present.
- **SignalR** — not present.
- **Hangfire / Quartz / background schedulers** — not present.
- **Feature flags** — none.
- **Health checks** — none.
- **CORS** — not configured.
- **Rate limiting** — not configured.
- **`UserSecretsId`** — not set in csproj.
- **Auto-migration at startup** — not present (`dotnet ef database update` required).

## 12. Secrets Management

**Current state (finding):** secrets are stored in-repo:
- `appsettings.json` → `Ai:ApiKey` populated.
- `appsettings.Development.json` → `Email:SenderPassword` populated with a real Gmail App Password.

**Recommendation (no code change performed):** rotate these secrets and move them to:
- .NET user secrets (add a `UserSecretsId` to the csproj) for local dev, and
- environment variables / a secret store (Azure Key Vault, etc.) for other environments.

Encrypted-at-rest user secrets (`PersonalGeminiApiKey`, credential `Secret`) rely on DataProtection keys — persist/share these keys in multi-instance deployments.

---

_See also: [07-security.md](07-security.md), [11-background-jobs.md](11-background-jobs.md)._

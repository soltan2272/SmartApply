# Service Flow

## Layer Topology

Phase 1 introduced the Repository tier and the Database. The full topology is now:

```
Controller   →   Service / Quota / Repository   →   ApplicationDbContext   →   SQL Server
                                              ↘   External system (Gemini / LinkedIn / DuckDuckGo / SMTP)
```

External-system calls bypass the Repository tier; nothing about an external response is persisted.

## Request Flows

### Flow A — Analyze a job (POST /Job/Analyze)

```mermaid
sequenceDiagram
    participant User as Browser
    participant JobCtrl as JobController
    participant ProfRepo as IUserProfileRepository
    participant Quota as IAiQuotaService
    participant UsageRepo as IAiUsageRepository
    participant Scraper as IJobScraperService
    participant Ai as IAiService
    participant LinkedIn
    participant Gemini

    User->>JobCtrl: POST /Job/Analyze (JobInput)
    JobCtrl->>ProfRepo: GetByUserIdAsync(userId)
    ProfRepo-->>JobCtrl: UserProfile (with optional PersonalGeminiApiKey)
    JobCtrl->>Quota: TryConsumeAsync(userId, hasPersonalKey)
    Quota->>UsageRepo: TryIncrementIfBelowLimitAsync(userId, limit)
    UsageRepo-->>Quota: newCount or null
    Quota-->>JobCtrl: (allowed, used, limit)
    alt Quota denied (no personal key)
        JobCtrl-->>User: Index view with quota-exceeded error
    else allowed
        opt JobUrl present
            JobCtrl->>Scraper: ScrapeJobDescriptionAsync(url)
            Scraper->>LinkedIn: GET <jobUrl>
            LinkedIn-->>Scraper: HTML
            Scraper-->>JobCtrl: cleaned text
        end
        JobCtrl->>Ai: AnalyzeJobAsync(desc, overrideKey?)
        Ai->>Gemini: POST :generateContent
        Gemini-->>Ai: JSON
        Ai-->>JobCtrl: JobAnalysisResult
        JobCtrl->>Ai: GenerateEmailAsync(analysis, profile, overrideKey?)
        Ai->>Gemini: POST :generateContent
        Gemini-->>Ai: JSON {subject, body}
        Ai-->>JobCtrl: EmailPreviewModel
        JobCtrl-->>User: View Preview.cshtml
    end
```

### Flow B — Save profile (POST /Profile)

```mermaid
sequenceDiagram
    participant User
    participant ProfileCtrl as ProfileController
    participant CvRepo as IUserCvFileRepository
    participant Repo as IUserProfileRepository
    participant Db as ApplicationDbContext

    User->>ProfileCtrl: POST /Profile (multipart, ≤ 5 MB)
    opt CV uploaded
        ProfileCtrl->>CvRepo: UpsertAsync(UserCvFile {bytes})
        CvRepo->>Db: SaveChangesAsync (UserCvFiles)
    end
    ProfileCtrl->>Repo: UpsertAsync(profile)
    Repo->>Db: SaveChangesAsync (UserProfiles)
    Db-->>Repo: rows affected
    Repo-->>ProfileCtrl: ok
    ProfileCtrl-->>User: 302 → /Profile
```

### Flow C — Bulk apply (POST /BulkApply/Send)

```mermaid
sequenceDiagram
    participant User
    participant Ctrl as BulkApplyController
    participant ProfileRepo as IUserProfileRepository
    participant CvRepo as IUserCvFileRepository
    participant CredentialRepo as IUserEmailCredentialRepository
    participant Sender as EmailSenderService
    participant Gmail as Gmail SMTP

    User->>Ctrl: POST recipients + template choice
    Ctrl->>ProfileRepo: GetByUserIdAsync
    Ctrl->>CvRepo: GetAsync (CV bytes)
    Ctrl->>CredentialRepo: GetByUserIdAsync (encrypted App Password)
    loop each recipient
        Ctrl->>Sender: SendEmailAsync(per-user SMTP account, CV attachment)
        Sender->>Gmail: STARTTLS authenticate + send
        Gmail-->>Sender: ok or SMTP error
    end
    Ctrl-->>User: View with per-recipient result summary
```

## Dependency Injection Structure

Source: [Program.cs](../Program.cs).

| Registration | Lifetime | Used by |
| --- | --- | --- |
| `AddControllersWithViews()` | n/a | MVC |
| `AddRazorPages()` | n/a | Identity UI |
| `AddDbContext<ApplicationDbContext>(...)` | Scoped | Repositories |
| `AddDataProtection()` | Singleton | `EncryptedStringConverter` |
| `AddDefaultIdentity<ApplicationUser>()` + `AddEntityFrameworkStores<ApplicationDbContext>()` | (multiple lifetimes) | Identity UI, controllers |
| `AddAuthentication().AddGoogle(...)` (when configured) | n/a | External login |
| `Configure<AiSettings>(...)` / `Configure<EmailSettings>(...)` | Singleton (`IOptions`) | `GeminiAiService`, `EmailSenderService`, `AiQuotaService` |
| `AddHttpClient<IJobScraperService, JobScraperService>()` | Transient (typed client) | `JobController` |
| `AddHttpClient<IAiService, GeminiAiService>()` | Transient (typed client) | `JobController` |
| `AddScoped<IEmailSenderService, EmailSenderService>()` | Scoped | `JobController`, `BulkApplyController`, Identity email adapter |
| `AddScoped<IUserProfileRepository, UserProfileRepository>()` | Scoped | `JobController`, `ProfileController` |
| `AddScoped<IUserCvFileRepository, UserCvFileRepository>()` | Scoped | `JobController`, `ProfileController` |
| `AddScoped<IUserEmailCredentialRepository, UserEmailCredentialRepository>()` | Scoped | `ProfileController`, `BulkApplyController` |
| `AddScoped<IAiUsageRepository, AiUsageRepository>()` | Scoped | `AiQuotaService` |
| `AddScoped<IAiQuotaService, AiQuotaService>()` | Scoped | `JobController`, `ProfileController` |
| `AddMemoryCache()` | Singleton | `JobController` |
| `AddSession()` | Singleton store | TempData |
| `ConfigureApplicationCookie(...)` | n/a | Identity cookie paths |

## Pipeline (HTTP middleware)

From [Program.cs](../Program.cs):

1. (Production) `UseExceptionHandler("/Home/Error")` and `UseHsts()`
2. `UseHttpsRedirection()`
3. `UseRouting()`
4. `UseSession()`
5. **`UseAuthentication()`** (new)
6. `UseAuthorization()`
7. `MapStaticAssets()`
8. `MapControllerRoute(default = "Home/Index/{id?}")`
9. `MapRazorPages()` (Identity UI)

Authentication MUST precede Authorization, which is now required (not just registered) because of `[Authorize]` on `JobController` and `ProfileController`.

## Constructor Dependency Map

| Type | Depends on |
| --- | --- |
| `JobController` | `IJobScraperService`, `IAiService`, `IEmailSenderService`, `IUserProfileRepository`, `IUserCvFileRepository`, `IAiQuotaService`, `IMemoryCache` |
| `BulkApplyController` | `IUserProfileRepository`, `IUserCvFileRepository`, `IUserEmailCredentialRepository`, `IEmailSenderService` |
| `ProfileController` | `IUserProfileRepository`, `IUserCvFileRepository`, `IUserEmailCredentialRepository`, `IAiQuotaService`, `UserManager<ApplicationUser>` |
| `GeminiAiService` | `HttpClient`, `IOptions<AiSettings>` |
| `JobScraperService` | `HttpClient` |
| `EmailSenderService` | `IOptions<EmailSettings>` |
| `AiQuotaService` | `IAiUsageRepository`, `IOptions<AiSettings>` |
| `UserProfileRepository` | `ApplicationDbContext` |
| `UserCvFileRepository` | `ApplicationDbContext` |
| `UserEmailCredentialRepository` | `ApplicationDbContext` |
| `AiUsageRepository` | `ApplicationDbContext` |
| `ApplicationDbContext` | `DbContextOptions`, optional `IDataProtectionProvider` |

---

Last reviewed: 2026-06-01

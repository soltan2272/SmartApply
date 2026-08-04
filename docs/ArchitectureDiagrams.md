# Architecture Diagrams

## 1. Component Diagram

```mermaid
graph TB
    Browser[Browser]

    subgraph WebApp[ASP.NET Core MVC]
        IdentityUI[Identity Razor Pages]
        HomeCtrl[HomeController]
        JobCtrl[JobController]
        ProfileCtrl[ProfileController]

        subgraph Services
            IScraper[IJobScraperService]
            IAi[IAiService]
            IEmail[IEmailSenderService]
            IQuota[IAiQuotaService]
            Scraper[JobScraperService]
            Gemini[GeminiAiService]
            Emailer[EmailSenderService]
            Quota[AiQuotaService]
        end

        subgraph Repos[Repository Layer]
            IProfileRepo[IUserProfileRepository]
            IUsageRepo[IAiUsageRepository]
            ProfileRepo[UserProfileRepository]
            UsageRepo[AiUsageRepository]
        end

        DbCtx[ApplicationDbContext]
        Cache[(IMemoryCache)]
        DataProt[IDataProtectionProvider]
    end

    Sql[(SQL Server)]
    LinkedIn[LinkedIn jobs-guest]
    Duck[DuckDuckGo HTML]
    GeminiApi[Google Gemini REST]
    Smtp[Gmail SMTP]
    GoogleOAuth[Google OAuth 2.0]

    Browser <--> IdentityUI
    Browser <--> HomeCtrl
    Browser <--> JobCtrl
    Browser <--> ProfileCtrl

    IdentityUI <--> GoogleOAuth

    IScraper -.implements.-> Scraper
    IAi -.implements.-> Gemini
    IEmail -.implements.-> Emailer
    IQuota -.implements.-> Quota
    IProfileRepo -.implements.-> ProfileRepo
    IUsageRepo -.implements.-> UsageRepo

    JobCtrl --> IScraper
    JobCtrl --> IAi
    JobCtrl --> IEmail
    JobCtrl --> IQuota
    JobCtrl --> IProfileRepo
    JobCtrl --> Cache

    ProfileCtrl --> IProfileRepo
    ProfileCtrl --> IQuota

    Quota --> IUsageRepo
    ProfileRepo --> DbCtx
    UsageRepo --> DbCtx
    DbCtx --> Sql
    DbCtx --> DataProt

    Scraper -->|HTTPS| LinkedIn
    Scraper -->|HTTPS| Duck
    Gemini -->|HTTPS| GeminiApi
    Emailer -->|StartTls 587| Smtp
```

## 2. Request Flow Diagrams

### 2a. Analyze flow (POST /Job/Analyze)

```mermaid
sequenceDiagram
    participant Browser
    participant JobCtrl as JobController
    participant ProfRepo as UserProfileRepository
    participant Quota as AiQuotaService
    participant UsageRepo as AiUsageRepository
    participant Scraper
    participant Ai as GeminiAiService
    participant Sql as SQL Server
    participant Gemini

    Browser->>JobCtrl: POST /Job/Analyze
    JobCtrl->>ProfRepo: GetByUserIdAsync
    ProfRepo->>Sql: SELECT UserProfiles
    Sql-->>ProfRepo: row
    ProfRepo-->>JobCtrl: UserProfile
    JobCtrl->>Quota: TryConsumeAsync
    Quota->>UsageRepo: TryIncrementIfBelowLimitAsync
    UsageRepo->>Sql: SELECT/UPDATE/INSERT AiUsages
    Sql-->>UsageRepo: result
    UsageRepo-->>Quota: newCount or null
    Quota-->>JobCtrl: allowed?
    alt allowed
        opt URL provided
            JobCtrl->>Scraper: ScrapeJobDescriptionAsync
            Scraper-->>JobCtrl: cleaned text
        end
        JobCtrl->>Ai: AnalyzeJobAsync(desc, overrideKey?)
        Ai->>Gemini: POST :generateContent
        Gemini-->>Ai: JSON
        Ai-->>JobCtrl: JobAnalysisResult
        JobCtrl->>Ai: GenerateEmailAsync(analysis, profile, overrideKey?)
        Ai->>Gemini: POST :generateContent
        Gemini-->>Ai: JSON
        Ai-->>JobCtrl: EmailPreviewModel
        JobCtrl-->>Browser: View Preview
    else denied
        JobCtrl-->>Browser: Index view, quota error
    end
```

### 2b. Profile save flow (POST /Profile)

```mermaid
sequenceDiagram
    participant Browser
    participant Ctrl as ProfileController
    participant CvRepo as UserCvFileRepository
    participant Repo as UserProfileRepository
    participant Sql as SQL Server

    Browser->>Ctrl: POST /Profile (multipart, ≤ 5 MB CV)
    opt CV file present
        Ctrl->>CvRepo: UpsertAsync(UserCvFile {bytes})
        CvRepo->>Sql: INSERT or UPDATE UserCvFiles
        Sql-->>CvRepo: ok
    end
    Ctrl->>Repo: UpsertAsync(UserProfile)
    Repo->>Sql: INSERT or UPDATE UserProfiles
    Sql-->>Repo: ok
    Repo-->>Ctrl: ok
    Ctrl-->>Browser: 302 → /Profile
```

### 2c. Login flow with Google

```mermaid
sequenceDiagram
    participant Browser
    participant Identity as Identity Razor Page
    participant Google as Google OAuth
    participant Sql as SQL Server

    Browser->>Identity: GET /Identity/Account/Login
    Identity-->>Browser: Login form
    Browser->>Identity: Click "Google"
    Identity->>Google: 302 → consent screen
    Google-->>Browser: consent
    Google->>Identity: GET /signin-google?code=...
    Identity->>Google: exchange code for tokens
    Google-->>Identity: access_token + profile
    Identity->>Sql: INSERT or SELECT AspNetUsers + AspNetUserLogins
    Sql-->>Identity: user row
    Identity-->>Browser: 302 → / with auth cookie
```

## 3. Entity Relationship Diagram

The full schema lives in [DatabaseStructure.md](DatabaseStructure.md). Compact view:

```mermaid
erDiagram
    AspNetUsers ||--o| UserProfiles : "1:1"
    AspNetUsers ||--o| UserCvFiles : "1:1"
    AspNetUsers ||--o{ AiUsages : "1:many"
    AspNetUsers ||--o{ AspNetUserLogins : has
    AspNetUsers ||--o{ AspNetUserClaims : has

    AspNetUsers {
        nvarchar Id PK
        nvarchar Email
        nvarchar PasswordHash
    }
    UserProfiles {
        nvarchar UserId PK_FK
        nvarchar FullName
        nvarchar ContactEmail
        nvarchar PersonalGeminiApiKey "encrypted"
    }
    UserCvFiles {
        nvarchar UserId PK_FK
        nvarchar FileName
        nvarchar ContentType
        bigint SizeBytes
        varbinary Content "max"
    }
    AiUsages {
        int Id PK
        nvarchar UserId FK
        int Year
        int Month
        int CallCount
    }
    AspNetUserLogins {
        nvarchar LoginProvider PK
        nvarchar ProviderKey PK
        nvarchar UserId FK
    }
```

---

Last reviewed: 2026-06-01

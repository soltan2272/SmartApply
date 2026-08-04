# 09 – Business Flows

> Each major workflow shown as: User Request → Controller → Service → Repository → Database → Response, with Mermaid sequence diagrams. Everything reflects the actual code paths.

## Table of Contents

- [1. AI Apply — Analyze Job](#1-ai-apply--analyze-job)
- [2. AI Apply — Send Email](#2-ai-apply--send-email)
- [3. Bulk Apply](#3-bulk-apply)
- [4. Job Search](#4-job-search)
- [5. Save Profile (+ CV + Credentials)](#5-save-profile--cv--credentials)
- [6. Download CV](#6-download-cv)
- [7. Register + Confirm Email](#7-register--confirm-email)
- [8. Login (with first-run prompt)](#8-login-with-first-run-prompt)

---

## 1. AI Apply — Analyze Job

**Path:** `POST /Job/Analyze`

```mermaid
sequenceDiagram
    actor U as User
    participant JC as JobController
    participant PR as UserProfileRepository
    participant QS as AiQuotaService
    participant UR as AiUsageRepository
    participant SC as JobScraperService
    participant AI as GeminiAiService
    participant CR as UserCvFileRepository
    participant DB as SQL Server

    U->>JC: POST Analyze (JobUrl / JobDescription)
    JC->>JC: validate at least one input
    JC->>PR: GetByUserIdAsync
    PR->>DB: SELECT UserProfiles
    alt profile missing / no FullName
        JC-->>U: 302 -> /Profile
    end
    JC->>QS: TryConsumeAsync(userId, hasPersonalKey)
    QS->>UR: TryIncrementIfBelowLimitAsync
    UR->>DB: INSERT/UPDATE AiUsages (atomic)
    alt quota exceeded
        JC-->>U: Index view with quota error
    end
    opt JobUrl provided
        JC->>SC: ScrapeJobDescriptionAsync(url)
        SC-->>JC: cleaned description
    end
    JC->>AI: AnalyzeJobAsync(description, apiKey)
    AI-->>JC: JobAnalysisResult
    JC->>AI: GenerateEmailAsync(analysis, profile, apiKey)
    AI-->>JC: EmailPreviewModel
    JC->>CR: GetMetadataAsync (HasCvOnFile)
    CR->>DB: SELECT UserCvFiles (metadata)
    JC-->>U: Preview view
```

**Notes:** quota is consumed **before** the AI calls; scrape/AI errors are caught and surfaced as model errors on the Index view.

---

## 2. AI Apply — Send Email

**Path:** `POST /Job/Send`

```mermaid
sequenceDiagram
    actor U as User
    participant JC as JobController
    participant PR as UserProfileRepository
    participant CR as UserCvFileRepository
    participant ES as EmailSenderService
    participant SMTP as Gmail SMTP
    participant DB as SQL Server

    U->>JC: POST Send (EmailPreviewModel)
    JC->>JC: ModelState valid?
    JC->>PR: GetByUserIdAsync
    PR->>DB: SELECT UserProfiles
    JC->>CR: GetAsync (bytes)
    CR->>DB: SELECT UserCvFiles (Content)
    JC->>ES: SendEmailAsync(to, subject, body, attachment)
    ES->>SMTP: connect + auth + send (global account)
    SMTP-->>ES: ok
    JC-->>U: 302 -> /Job/Success
```

**Notes:** sends from the **global** SMTP account. On failure, error shown on the Preview view.

---

## 3. Bulk Apply

**Path:** `POST /BulkApply/Send`

```mermaid
sequenceDiagram
    actor U as User
    participant BC as BulkApplyController
    participant PR as UserProfileRepository
    participant CR as UserCvFileRepository
    participant CE as UserEmailCredentialRepository
    participant ES as EmailSenderService
    participant SMTP as User Gmail SMTP
    participant DB as SQL Server

    U->>BC: POST Send (recipients, template)
    BC->>PR: GetByUserIdAsync
    BC->>CR: GetAsync (bytes)
    BC->>CE: GetByUserIdAsync (credential)
    BC->>BC: validate recipients (<=50), template, prerequisites
    alt invalid
        BC-->>U: Index view with errors
    end
    BC->>BC: build subject/body (auto or custom)
    loop each recipient
        BC->>ES: SendEmailAsync(userAccount, recipient, subject, body, cv)
        ES->>SMTP: connect + auth + send (user's Gmail)
        alt success
            BC->>BC: add result Success=true
        else exception
            BC->>BC: add result Success=false, Message
        end
    end
    BC-->>U: Index view with per-recipient Results
```

**Notes:** best-effort per recipient; the user's own Gmail credential is used; a new SMTP connection is opened per recipient.

---

## 4. Job Search

**Path:** `POST /Job/Search` then `GET /Job/Search?searchId&page`

```mermaid
sequenceDiagram
    actor U as User
    participant JC as JobController
    participant SC as JobScraperService
    participant LI as LinkedIn API
    participant DDG as DuckDuckGo
    participant MC as IMemoryCache

    U->>JC: POST Search (Title, ExperienceLevel, DatePosted)
    JC->>JC: validate Title required
    JC->>SC: SearchJobsAsync(...)
    par parallel
        SC->>LI: FetchLinkedInJobs
        SC->>DDG: FetchLinkedInPosts
    end
    SC-->>JC: merged List<JobSearchResultItem>
    JC->>MC: Set(searchId, viewModel, 15 min)
    JC-->>U: Search view (page 1)
    U->>JC: GET Search?searchId&page=2
    JC->>MC: TryGetValue(searchId)
    JC-->>U: Search view (page 2 from cache)
```

**Notes:** no database involvement; scraping failures return empty lists silently.

---

## 5. Save Profile (+ CV + Credentials)

**Path:** `POST /Profile/Index`

```mermaid
sequenceDiagram
    actor U as User
    participant PC as ProfileController
    participant CR as UserCvFileRepository
    participant PR as UserProfileRepository
    participant CE as UserEmailCredentialRepository
    participant DB as SQL Server

    U->>PC: POST Index (ProfileViewModel + optional CV)
    PC->>PC: ModelState valid?
    opt CV uploaded
        PC->>PC: validate size <=5MB + extension
        PC->>CR: UpsertAsync(UserCvFile bytes)
        CR->>DB: INSERT/UPDATE UserCvFiles
    end
    PC->>PR: UpsertAsync(UserProfile)
    PR->>DB: INSERT/UPDATE UserProfiles (encrypt key)
    opt sender email or app password provided
        PC->>CE: UpsertAsync(UserEmailCredential)
        CE->>DB: INSERT/UPDATE UserEmailCredentials (encrypt secret)
    end
    PC-->>U: 302 -> /Profile (TempData "Profile saved")
```

**Notes:** three separate `SaveChanges` calls (not a single transaction). PRG pattern on success.

---

## 6. Download CV

**Path:** `GET /Profile/Cv`

```mermaid
sequenceDiagram
    actor U as User
    participant PC as ProfileController
    participant CR as UserCvFileRepository
    participant DB as SQL Server
    U->>PC: GET Cv
    PC->>CR: GetAsync (bytes)
    CR->>DB: SELECT UserCvFiles
    alt found
        PC-->>U: FileResult (private,no-store)
    else null
        PC-->>U: 404
    end
```

---

## 7. Register + Confirm Email

**Path:** `POST /Identity/Account/Register` → `GET /Identity/Account/ConfirmEmail`

```mermaid
sequenceDiagram
    actor U as User
    participant R as RegisterModel
    participant UM as UserManager
    participant AS as UserAccountSetupService
    participant PR as UserProfileRepository
    participant IES as IdentityEmailSender
    participant ES as EmailSenderService

    U->>R: POST Register (email, password)
    R->>UM: CreateAsync(user, password)
    R->>AS: EnsureProfileAsync(userId, email)
    AS->>PR: Upsert profile (ContactEmail)
    R->>UM: GenerateEmailConfirmationTokenAsync
    R->>IES: SendConfirmationLinkAsync(callbackUrl)
    IES->>ES: SendHtmlEmailAsync (global SMTP)
    R-->>U: RegisterConfirmation page
    U->>UM: GET ConfirmEmail?userId&code
    UM-->>U: confirmed
```

---

## 8. Login (with first-run prompt)

**Path:** `POST /Identity/Account/Login`

```mermaid
sequenceDiagram
    actor U as User
    participant L as LoginModel
    participant SM as SignInManager
    participant AS as UserAccountSetupService
    participant CE as UserEmailCredentialRepository

    U->>L: POST Login (email, password)
    L->>SM: PasswordSignInAsync
    alt Succeeded
        L->>AS: EnsureProfileAsync
        L->>CE: GetByUserIdAsync
        alt no Gmail App Password
            L-->>U: 302 -> /Profile (prompt to add App Password)
        else
            L-->>U: 302 -> returnUrl
        end
    else IsNotAllowed (email unconfirmed)
        L-->>U: error "confirm your email"
    else failed
        L-->>U: "Invalid login attempt"
    end
```

---

_See also: [04-api-reference.md](04-api-reference.md) and the per-feature docs in [features/](features/)._

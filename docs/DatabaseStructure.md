# Database Structure

## Database engine

SQL Server. The default development connection string ([appsettings.json](../appsettings.json)) targets LocalDB (`(localdb)\MSSQLLocalDB`), database name `JobApplicationBot`. The same migration runs against any SQL Server 2017+ instance, including Azure SQL Database. Confidence: high.

## DbContext

Single context: `ApplicationDbContext` ([Data/ApplicationDbContext.cs](../Data/ApplicationDbContext.cs)).

It extends `IdentityDbContext<ApplicationUser>`, which automatically owns the seven Identity tables. Four domain DbSets are added:

- `DbSet<UserProfile> UserProfiles`
- `DbSet<AiUsage> AiUsages`
- `DbSet<UserCvFile> UserCvFiles`
- `DbSet<UserEmailCredential> UserEmailCredentials`

`OnModelCreating` performs five customizations:

1. Maps the 1-to-1 relationship from `ApplicationUser` to `UserProfile` (cascade delete).
2. Maps the 1-to-many relationship from `ApplicationUser` to `AiUsage` with a unique composite index on `(UserId, Year, Month)` named `IX_AiUsages_User_Year_Month`.
3. Maps the 1-to-1 relationship from `ApplicationUser` to `UserCvFile` (cascade delete) with `Content` mapped explicitly as `varbinary(max)`.
4. Maps the 1-to-1 relationship from `ApplicationUser` to `UserEmailCredential` (cascade delete).
5. Configures secret columns (`UserProfile.PersonalGeminiApiKey`, `UserEmailCredential.Secret`) to use `EncryptedStringConverter` ([Data/EncryptedStringConverter.cs](../Data/EncryptedStringConverter.cs)), which calls `IDataProtector.Protect/Unprotect` on every read/write.

## Tables

### Identity tables (standard ASP.NET Core Identity)

| Table | Purpose |
| --- | --- |
| `AspNetUsers` | Users (email, hashed password, security stamp, lockout state). PK `Id` (nvarchar(450)). |
| `AspNetRoles` | Roles. Currently unused — no roles seeded. |
| `AspNetUserClaims` | Per-user claims. |
| `AspNetUserLogins` | External login bindings (e.g., Google). PK `(LoginProvider, ProviderKey)`. |
| `AspNetUserRoles` | User → Role assignments. |
| `AspNetUserTokens` | OAuth refresh / 2FA tokens. |
| `AspNetRoleClaims` | Per-role claims. |

The `ApplicationUser` C# entity ([Data/Entities/ApplicationUser.cs](../Data/Entities/ApplicationUser.cs)) extends `IdentityUser` with no extra columns; it only adds a navigation property to `UserProfile`.

### Domain tables

#### `UserProfiles`

One-to-one with `AspNetUsers`. Holds user-supplied profile data used to personalize generated emails.

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `UserId` | nvarchar(450) | No | **PK / FK** → `AspNetUsers.Id`. ON DELETE CASCADE. |
| `FullName` | nvarchar(200) | No | |
| `Title` | nvarchar(200) | No | |
| `Phone` | nvarchar(50) | No | |
| `ContactEmail` | nvarchar(256) | No | Email shown in application signature; defaults to auth email but separately editable. |
| `SkillsSummary` | nvarchar(2000) | No | |
| `ExperienceSummary` | nvarchar(4000) | No | |
| `PersonalGeminiApiKey` | nvarchar(2000) | Yes | **Encrypted at rest** via `IDataProtector` (`EncryptedStringConverter`). When non-null, bypasses the freemium quota. |
| `CreatedAt` | datetime2 | No | UTC. |
| `UpdatedAt` | datetime2 | No | UTC. |

#### `AiUsages`

One-to-many with `AspNetUsers`. One row per (user, calendar month). Tracks freemium-quota consumption.

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `Id` | int IDENTITY | No | PK. |
| `UserId` | nvarchar(450) | No | FK → `AspNetUsers.Id`. ON DELETE CASCADE. |
| `Year` | int | No | UTC year. |
| `Month` | int | No | UTC month (1-12). |
| `CallCount` | int | No | Increments per Analyze action while quota remains. |
| `LastCallAt` | datetime2 | No | UTC. |

**Index**: `IX_AiUsages_User_Year_Month` UNIQUE on `(UserId, Year, Month)`. Enforces one row per user per month and protects the increment loop in `AiUsageRepository.TryIncrementIfBelowLimitAsync` ([Data/Repositories/AiUsageRepository.cs](../Data/Repositories/AiUsageRepository.cs)) from double-insert races.

#### `UserCvFiles`

One-to-one with `AspNetUsers`. Holds the raw CV bytes used as an email attachment. Kept as a separate table from `UserProfiles` so that the multi-megabyte `Content` column is **never** loaded by the hot read path (`IUserProfileRepository.GetByUserIdAsync` is called on every Analyze and Send).

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `UserId` | nvarchar(450) | No | **PK / FK** → `AspNetUsers.Id`. ON DELETE CASCADE. |
| `FileName` | nvarchar(255) | No | Original upload file name (used for the email attachment and the download link). |
| `ContentType` | nvarchar(100) | No | MIME type captured at upload time. Defaults to `application/octet-stream` when the browser does not provide one. |
| `SizeBytes` | bigint | No | Length of `Content` in bytes. Capped to 5 MB at the controller (and 6 MB at Kestrel + multipart form for defense in depth). |
| `Content` | varbinary(max) | No | The actual CV bytes. |
| `UploadedAt` | datetime2 | No | UTC. Stamped on insert and on every update. |

The repository ([Data/Repositories/UserCvFileRepository.cs](../Data/Repositories/UserCvFileRepository.cs)) projects metadata-only reads (`GetMetadataAsync`) so SQL Server never streams the BLOB unless the bytes are actually needed (download, email send).

#### `UserEmailCredentials`

One-to-one with `AspNetUsers`. Stores per-user outbound email sender settings for `/BulkApply`; currently Gmail SMTP App Passwords, with the provider column ready for a future Gmail OAuth implementation.

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| `UserId` | nvarchar(450) | No | **PK / FK** → `AspNetUsers.Id`. ON DELETE CASCADE. |
| `Provider` | nvarchar(50) | No | `GmailSmtp` now; future value can be `GmailOAuth`. |
| `SenderEmail` | nvarchar(256) | No | Gmail address used as SMTP username / From address. |
| `SenderName` | nvarchar(200) | No | Display name in the From header. |
| `SmtpHost` | nvarchar(200) | No | Defaults to `smtp.gmail.com`. |
| `SmtpPort` | int | No | Defaults to `587`. |
| `UseStartTls` | bit | No | Defaults to true. |
| `Secret` | nvarchar(2000) | Yes | **Encrypted at rest**. For `GmailSmtp`, this is the Gmail App Password. |
| `CreatedAt` | datetime2 | No | UTC. |
| `UpdatedAt` | datetime2 | No | UTC. |

## Entity Relationship Diagram

```mermaid
erDiagram
    AspNetUsers ||--o| UserProfiles : "1:1"
    AspNetUsers ||--o| UserCvFiles : "1:1"
    AspNetUsers ||--o| UserEmailCredentials : "1:1"
    AspNetUsers ||--o{ AiUsages : "1:many"
    AspNetUsers ||--o{ AspNetUserClaims : has
    AspNetUsers ||--o{ AspNetUserLogins : has
    AspNetUsers ||--o{ AspNetUserTokens : has
    AspNetUsers ||--o{ AspNetUserRoles : has
    AspNetRoles ||--o{ AspNetUserRoles : has
    AspNetRoles ||--o{ AspNetRoleClaims : has

    AspNetUsers {
        nvarchar Id PK
        nvarchar Email
        bit EmailConfirmed
        nvarchar PasswordHash
    }
    UserProfiles {
        nvarchar UserId PK_FK
        nvarchar FullName
        nvarchar Title
        nvarchar ContactEmail
        nvarchar PersonalGeminiApiKey "encrypted"
        datetime2 CreatedAt
        datetime2 UpdatedAt
    }
    UserCvFiles {
        nvarchar UserId PK_FK
        nvarchar FileName
        nvarchar ContentType
        bigint SizeBytes
        varbinary Content "max - the CV bytes"
        datetime2 UploadedAt
    }
    UserEmailCredentials {
        nvarchar UserId PK_FK
        nvarchar Provider
        nvarchar SenderEmail
        nvarchar SenderName
        nvarchar Secret "encrypted"
    }
    AiUsages {
        int Id PK
        nvarchar UserId FK
        int Year
        int Month
        int CallCount
        datetime2 LastCallAt
    }
```

## Foreign Keys (domain only)

| FK | From | To | On delete |
| --- | --- | --- | --- |
| `FK_UserProfiles_AspNetUsers_UserId` | `UserProfiles.UserId` | `AspNetUsers.Id` | CASCADE |
| `FK_UserCvFiles_AspNetUsers_UserId` | `UserCvFiles.UserId` | `AspNetUsers.Id` | CASCADE |
| `FK_UserEmailCredentials_AspNetUsers_UserId` | `UserEmailCredentials.UserId` | `AspNetUsers.Id` | CASCADE |
| `FK_AiUsages_AspNetUsers_UserId` | `AiUsages.UserId` | `AspNetUsers.Id` | CASCADE |

Cascade delete means deleting an `AspNetUsers` row automatically removes the user's profile, stored CV, email sender credential, and all monthly usage counters. Confidence: high (verified in the generated migrations).

## Migrations

- `Migrations/20260601*_InitialIdentityAndProfile.cs` — creates all Identity tables, `UserProfiles`, `AiUsages`, and indexes.
- `Migrations/20260601*_StoreCvInDb.cs` — drops `UserProfiles.CvFilePath` (CVs are no longer stored on disk) and creates `UserCvFiles` with the cascade-delete FK.
- `Migrations/20260604*_AddUserEmailCredentialForBulkApply.cs` — drops the unused `UserProfiles.LinkedIn` column and creates `UserEmailCredentials`.
- To apply: `dotnet ef database update`.
- To roll back the CV change: `dotnet ef database update InitialIdentityAndProfile`.

## Operational notes

- **Read Committed Snapshot Isolation** is enabled by SQL Server LocalDB during initial migration (`ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON`). This avoids most reader/writer blocking on the small per-user counters. Confidence: high.
- The `IX_AiUsages_User_Year_Month` unique index is the **only** non-Identity, non-PK index in the schema. Job analysis history is currently not persisted.
- CVs live in SQL Server (`UserCvFiles.Content`, `varbinary(max)`). At thousands of users this is fine; at millions, backups grow linearly with CV count × 5 MB and Phase 3 should move CVs to dedicated blob storage. See [TechnicalDebt.md](TechnicalDebt.md).

---

Last reviewed: 2026-06-01

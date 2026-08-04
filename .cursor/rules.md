# Cursor Rules — SmartApply Hub (JobApplicationBot)

These rules apply to all AI-assisted work in this repository. Follow them for every task.

## Mandatory Workflow

1. **Always read [`docs/AI_CONTEXT.md`](../docs/AI_CONTEXT.md) before answering.** It is the authoritative summary of the project.
2. **Read the relevant feature documentation before implementing anything.** Feature docs live in [`docs/features/`](../docs/features/) (e.g. `AiApply.md`, `BulkApply.md`, `Profile.md`, `Authentication.md`, `JobSearch.md`, `AiQuota.md`). Also consult the numbered docs `docs/01`–`docs/12` as needed.
3. **Follow the existing architecture:** `Controllers → Services → Repositories → ApplicationDbContext → SQL Server`. See [`docs/12-architecture.md`](../docs/12-architecture.md).
4. **Never duplicate business logic.** If a rule/behavior already exists, reuse it.
5. **Reuse existing services whenever possible** — `IAiService`, `IJobScraperService`, `IEmailSenderService`, `IAiQuotaService`, `IUserAccountSetupService`, and the repositories — instead of writing parallel implementations.
6. **Follow existing coding conventions** (see below and [`docs/AI_CONTEXT.md`](../docs/AI_CONTEXT.md#6-coding-standards)).
7. **Respect SOLID principles.** Keep responsibilities single and depend on interfaces.
8. **Keep controllers thin.** Controllers orchestrate HTTP, binding, and validation dispatch only.
9. **Put business logic inside services** (or repositories for data access). Do not access `ApplicationDbContext` directly from controllers.
10. **Update the documentation after every feature implementation** — the matching `docs/*` and feature file, plus `docs/AI_CONTEXT.md` if architecture/entities/services change.
11. **Never make assumptions about business rules.** If a rule is not in the code or docs, ask or state the uncertainty explicitly — do not invent behavior.
12. **Search the existing codebase before creating new classes or methods.** Prefer extending/reusing existing types.
13. **Maintain consistency with the current project structure** (folder layout, namespaces, naming).

## Coding Conventions (enforce)

- .NET 9, C# with **nullable enabled** and **implicit usings**; file-scoped namespaces.
- Interfaces prefixed `I`; async methods suffixed `Async` and accept a `CancellationToken ct`.
- One repository per aggregate (`{Entity}Repository` + interface); reads use `AsNoTracking()`, writes are load-then-mutate upserts + `SaveChangesAsync`.
- All timestamps in **UTC** (`DateTime.UtcNow`).
- Resolve the current user via `User.FindFirstValue(ClaimTypes.NameIdentifier)` and **scope all data by user id**.
- All POST actions require `[ValidateAntiForgeryToken]`; feature controllers require `[Authorize]`.
- Strongly-typed config via `IOptions<AiSettings>` / `IOptions<EmailSettings>` — do not read `IConfiguration` ad hoc in business code.
- Persist secrets ONLY through `EncryptedStringConverter`-mapped columns (never plaintext). Do not log secrets.
- Choose the correct `IEmailSenderService.SendEmailAsync` overload: **global** account for single/identity email, **per-user** `EmailSenderAccount` for bulk.

## Guardrails

- **Do not** commit secrets. Never hardcode API keys, passwords, or connection strings (see [`docs/code-quality.md`](../docs/code-quality.md) findings S1).
- **Do not** introduce JWT, roles, policies, CQRS, Hangfire, Redis, or SignalR unless the task explicitly requires it — none exist today; adding them is an architectural change to be called out.
- **Do not** bypass the AI quota (`IAiQuotaService`) when adding AI-consuming features.
- When fetching user-supplied URLs server-side, consider the existing **SSRF** risk (finding S2) and prefer adding a guard.
- Register any new service/repository in `Program.cs` with the correct lifetime (Scoped for DB-bound; typed `HttpClient` for external HTTP).
- Add an EF migration for any entity/schema change; migrations are applied manually (`dotnet ef database update`).

## Definition of Done

- Code compiles, follows conventions, and reuses existing abstractions.
- New/changed behavior is covered by the correct layer (service/repository).
- Relevant docs updated (`docs/*`, `docs/features/*`, and `docs/AI_CONTEXT.md` if needed).
- No secrets added; no linter errors introduced.

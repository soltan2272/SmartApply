# 11 – Background Jobs & Async Processing

> Analysis of background/async processing in the codebase.

## Table of Contents

- [1. Summary](#1-summary)
- [2. Hosted Services](#2-hosted-services)
- [3. Hangfire Jobs](#3-hangfire-jobs)
- [4. Scheduled Jobs](#4-scheduled-jobs)
- [5. Queue Processing](#5-queue-processing)
- [6. Notifications](#6-notifications)
- [7. Emails](#7-emails)
- [8. In-Request Async & Parallelism](#8-in-request-async--parallelism)
- [9. Caching (background-adjacent)](#9-caching-background-adjacent)

---

## 1. Summary

**This application has no background job infrastructure.** There are:

- ❌ No `IHostedService` / `BackgroundService`.
- ❌ No Hangfire, Quartz.NET, or any scheduler.
- ❌ No message queue (RabbitMQ, Azure Service Bus, etc.).
- ❌ No SignalR / real-time notifications.
- ❌ No cron/recurring jobs.

All work happens **synchronously within the HTTP request** (using `async`/`await` for I/O). This section documents that explicitly so future contributors do not assume otherwise.

## 2. Hosted Services

None registered. `Program.cs` contains no `AddHostedService<>`.

## 3. Hangfire Jobs

Not present. No Hangfire package, dashboard, server, or job definitions.

## 4. Scheduled Jobs

None. The monthly AI quota is **not** reset by a scheduled job — it is derived on-read from the `(Year, Month)` columns of `AiUsages`, so a new month naturally starts a fresh counter row (see [06-data-access.md](06-data-access.md)).

## 5. Queue Processing

None. Email sending (single and bulk) is performed inline during the request:

- **Single Apply** (`POST /Job/Send`): one SMTP send in-request.
- **Bulk Apply** (`POST /BulkApply/Send`): a `foreach` loop over recipients, each opening its own SMTP connection **synchronously within the request**. Large batches keep the request open for the duration.

> This is a scalability/UX limitation (long requests for big batches). A future improvement would move bulk sending to a background queue. Noted in [code-quality.md](code-quality.md).

## 6. Notifications

No push/real-time notifications. User feedback is delivered via:
- `TempData` messages (e.g. "Profile saved", "confirm your email").
- `ModelState` errors rendered in views.
- Per-recipient `BulkApplySendResult` list on the Bulk Apply page.

## 7. Emails

Emails are sent **synchronously** via `EmailSenderService` (MailKit) in three contexts:
1. Identity confirmation / password-reset emails (global SMTP) — triggered from Identity Razor Pages.
2. Single job application email (global SMTP) — `POST /Job/Send`.
3. Bulk application emails (per-user Gmail) — `POST /BulkApply/Send`.

There is no email queue, retry policy, or outbox pattern.

## 8. In-Request Async & Parallelism

The only intentional parallelism is in `JobScraperService.SearchJobsAsync`, which runs LinkedIn jobs and LinkedIn posts fetches concurrently:

```csharp
var jobsTask = FetchLinkedInJobs(...);
var postsTask = FetchLinkedInPosts(...);
await Task.WhenAll(jobsTask, postsTask);
```

All other async is straightforward awaited I/O (DB, HTTP, SMTP). `CancellationToken`s are threaded through most controller/service/repository methods.

## 9. Caching (background-adjacent)

`IMemoryCache` stores job-search results for 15 minutes (key `{userId}:{guid}`). This is a passive cache with sliding retrieval by `searchId` — there is no background eviction job beyond the built-in expiration.

---

_See also: [05-services.md](05-services.md), [08-configuration.md](08-configuration.md)._

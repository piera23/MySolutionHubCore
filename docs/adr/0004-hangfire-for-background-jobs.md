# ADR-0004: Hangfire for Background Job Scheduling

**Date:** 2026-03-10
**Status:** Accepted

---

## Context

The platform requires two categories of background work:
1. **Recurring maintenance jobs** — token cleanup, tenant migration checks.
2. **Triggered provisioning jobs** — running EF migrations on newly created tenant databases (cannot block the HTTP request).

We evaluated:
- **`IHostedService` / `BackgroundService`** — in-process .NET background services.
- **Hangfire** — persistent job queue backed by PostgreSQL.
- **Quartz.NET** — full-featured job scheduler.

---

## Decision

We use **Hangfire with the `Hangfire.PostgreSql` storage adapter** for all background job scheduling.

`IHostedService` is still used for the `RefreshTokenCleanupService` to demonstrate the pattern, but the service also exposes `TriggerManualRunAsync` so it can be invoked by Hangfire recurring jobs in production. `TenantMigrationHostedService` follows the same pattern.

Hangfire is registered in **all environments** (including Development) so that Docker-based development works consistently. The dashboard is exposed only in production at `/hangfire`, protected by the `HangfireAdminAuthFilter` (Admin role required).

Recurring jobs are registered in `Program.cs` on non-Development environments:
- `tenant-migrations` — every 12 hours (`0 */12 * * *`)
- `refresh-token-cleanup` — daily at 03:00 UTC (`0 3 * * *`)

Hangfire uses the **same PostgreSQL master database** connection string (schema `hangfire`). No additional database is required.

---

## Consequences

**Positive:**
- **Persistent job queue.** Jobs survive application restarts. If the API crashes mid-migration, Hangfire retries the job on the next startup.
- **Dashboard.** The `/hangfire` UI provides job history, retry counts, and manual triggers without custom tooling.
- **PostgreSQL storage.** No Redis or additional infrastructure needed. The master DB already exists.
- **Fire-and-forget enqueue.** `BackgroundJob.Enqueue<TService>(...)` from the provisioning controller allows the HTTP response to return immediately while the tenant database is created in the background.
- **Retry policies.** Failed jobs are automatically retried with exponential backoff.

**Negative:**
- **Master DB dependency.** Hangfire tables live in the master database. If the master DB is unavailable, no background jobs run. Accepted as consistent with the overall architecture.
- **Worker count fixed at 2.** Configured with `WorkerCount = 2` and two queues (`migrations`, `default`). This may need tuning for high-tenant-count deployments.
- **Dashboard auth is custom.** `HangfireAdminAuthFilter` must be kept in sync with the authorization policy. A future improvement could delegate to the standard ASP.NET Core authorization middleware.

---

## Alternatives Rejected

**Pure `IHostedService`** was rejected for tenant migration jobs because:
- In-process services cannot survive application restarts. A tenant provisioning job interrupted by a crash would leave the tenant database in a broken state.
- No built-in retry logic or job history.

**Quartz.NET** was rejected because:
- Heavier API surface than needed.
- No built-in web dashboard.
- PostgreSQL persistence requires additional setup compared to Hangfire.PostgreSql's drop-in integration.

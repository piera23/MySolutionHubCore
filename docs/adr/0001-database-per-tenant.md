# ADR-0001: Database-per-Tenant Multi-Tenancy Strategy

**Date:** 2026-03-01
**Status:** Accepted

---

## Context

MySolutionHubCore is a multi-tenant SaaS platform. We needed to decide how to isolate tenant data. The three main strategies considered were:

1. **Shared database, shared schema** — all tenants in one DB, rows tagged with `TenantId`.
2. **Shared database, separate schemas** — one DB, one PostgreSQL schema per tenant.
3. **Database per tenant** — each tenant gets a dedicated PostgreSQL database.

The platform's roadmap includes features with different data volumes per tenant, strict data isolation requirements, and the possibility of tenant-specific schema evolution.

---

## Decision

We chose **database-per-tenant** (option 3).

A separate **master database** (`mysolutionhub_master`) holds the tenant registry and stores an AES-256 encrypted connection string for each tenant database. All application data lives in the per-tenant database, resolved at request time via the `TenantResolutionMiddleware`.

---

## Consequences

**Positive:**
- **Hard data isolation.** A bug or a compromised query in one tenant cannot expose another tenant's data. Regulatory compliance (GDPR, per-customer data residency) is simpler to demonstrate.
- **Independent schema evolution.** A tenant can be migrated to a newer schema version independently. Failed migrations affect only one tenant.
- **Independent scaling.** High-volume tenants can be moved to a different PostgreSQL instance without affecting others.
- **Backup and restore granularity.** Each tenant's database can be backed up and restored individually.

**Negative:**
- **Operational complexity.** Many databases to monitor, back up, and migrate. Addressed by the `TenantMigrationHostedService` and Hangfire recurring jobs.
- **Connection overhead.** EF Core creates a `DbContext` per request pointing at a different database. Mitigated with Npgsql connection pooling per connection string.
- **Cross-tenant queries impossible.** Admin analytics that span tenants must be performed by querying each tenant DB in sequence. Accepted trade-off.
- **Master database is a SPOF.** If the master DB is unavailable, tenant resolution fails for all tenants. Mitigated with in-memory caching (`IMemoryCache`) of resolved tenant connections.

---

## Alternatives Rejected

**Shared schema with `TenantId` column** was rejected because:
- A missing `WHERE TenantId = ?` filter in any query leaks data across tenants.
- EF Core global query filters reduce but do not eliminate this risk.
- Poor fit for GDPR data-deletion requirements (deleting one tenant's data requires deleting rows scattered across all tables).

**Separate schemas** was rejected because PostgreSQL schema-based isolation is weaker than database-level isolation, and EF Core's multi-schema support adds significant migration complexity with little benefit over database-per-tenant at our scale.

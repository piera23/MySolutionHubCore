# ADR-0006: Soft Delete via EF Core Global Query Filters

**Date:** 2026-03-09
**Status:** Accepted

---

## Context

Most entities in the tenant database (users, messages, notifications, activity events, etc.) should not be physically deleted from the database when a user or admin removes them. Requirements:

- Deleted records must not appear in API responses.
- Data must remain available for audit, support, and potential recovery.
- No manual `WHERE IsDeleted = false` must be added to every query.

---

## Decision

All major tenant-database entities have an `IsDeleted boolean NOT NULL DEFAULT false` column. EF Core **global query filters** are applied at the `DbContext` level to automatically append `WHERE "IsDeleted" = false` to every LINQ query on those entities.

Physical deletion (hard delete) is only used for:
- Refresh token cleanup (revoked/expired tokens deleted by the Hangfire job).
- Hangfire internal tables (managed by the library).

The master database tables (`Tenants`, `TenantConnections`, etc.) do not use soft delete because tenant lifecycle is managed explicitly by admin operations.

---

## Consequences

**Positive:**
- **Transparent to application code.** No developer needs to remember to filter by `IsDeleted`. The filter is declared once in `BaseAppDbContext` and applies everywhere.
- **Audit trail.** Deleted records remain in the database and are queryable by admin tooling that bypasses the global filter using `.IgnoreQueryFilters()`.
- **Safe user deletion.** Deleting a user sets `IsDeleted = true`; all their related records (messages, reactions, notifications) remain and can be reviewed before a full GDPR erasure.
- **Referential integrity preserved.** FK constraints are not violated because rows are never physically removed during soft delete.

**Negative:**
- **Table growth.** Deleted rows accumulate over time. A future maintenance job could archive or hard-delete old soft-deleted rows after a retention period.
- **`IgnoreQueryFilters()` footgun.** Any code using `IgnoreQueryFilters()` will return deleted records. Usage is intentionally limited to admin diagnostic endpoints.
- **Unique index conflicts.** A soft-deleted email cannot be re-registered unless the unique index is defined as a partial index (`WHERE "IsDeleted" = false` or `WHERE "Email" IS NOT NULL`). The current email unique index does not account for soft delete — this is a known limitation to address if user account recovery is added.

---

## Alternatives Rejected

**Physical delete** was rejected because it permanently destroys audit-relevant data and makes GDPR erasure requests harder to verify (what was deleted, when, by whom?).

**Separate archive tables** were rejected due to complexity: maintaining parallel table structures and FK relationships doubles schema maintenance effort for no clear benefit at the current scale.

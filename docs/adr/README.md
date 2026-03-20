# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for MySolutionHubCore.

An ADR documents an architecturally significant decision: its context, the decision made, and its consequences. ADRs are immutable once accepted — superseded decisions get a new ADR marked as "Supersedes ADR-XXXX".

## Index

| ADR | Title | Status |
|---|---|---|
| [ADR-0001](0001-database-per-tenant.md) | Database-per-Tenant Multi-Tenancy Strategy | Accepted |
| [ADR-0002](0002-postgresql-as-primary-database.md) | PostgreSQL as the Primary Database Engine | Accepted |
| [ADR-0003](0003-jwt-with-refresh-token-rotation.md) | JWT Access Tokens with Refresh Token Rotation | Accepted |
| [ADR-0004](0004-hangfire-for-background-jobs.md) | Hangfire for Background Job Scheduling | Accepted |
| [ADR-0005](0005-signalr-for-realtime.md) | SignalR for Real-Time Communication | Accepted |
| [ADR-0006](0006-soft-delete-pattern.md) | Soft Delete via EF Core Global Query Filters | Accepted |
| [ADR-0007](0007-per-tenant-feature-flags.md) | Per-Tenant Feature Flags | Accepted |

## ADR Template

```markdown
# ADR-XXXX: Title

**Date:** YYYY-MM-DD
**Status:** Proposed | Accepted | Deprecated | Superseded by ADR-XXXX

---

## Context

What is the situation that forces us to make this decision?

## Decision

What did we decide?

## Consequences

What are the positive and negative consequences of this decision?

## Alternatives Rejected

What other options were considered and why were they rejected?
```

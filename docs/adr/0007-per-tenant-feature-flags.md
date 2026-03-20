# ADR-0007: Per-Tenant Feature Flags

**Date:** 2026-03-10
**Status:** Accepted

---

## Context

Different tenants may have purchased different tiers of the platform. Some tenants need only core identity features; others require the social feed, chat, or notifications. We needed a mechanism to:

- Enable or disable platform features per tenant.
- Enforce the restriction at the API level without code duplication.
- Allow features to carry optional JSON configuration.
- Be manageable at runtime by admins without a deployment.

---

## Decision

Feature flags are stored in the master database `TenantFeatures` table as `(TenantId, FeatureKey, IsEnabled, Config)` rows. Feature keys follow a namespaced convention: `social:feed`, `social:chat`, `social:notifications`.

A custom ASP.NET Core action filter attribute `[RequireFeature("social:feed")]` is applied to controller classes or actions. The filter:
1. Reads the resolved `TenantContext` from DI.
2. Checks `TenantFeatures` for the feature key (via the cached tenant data).
3. Returns `403 Forbidden` with a descriptive error if the feature is disabled.

Features are managed at runtime via `PUT /api/admin/tenants/{tenantId}/features/{featureKey}`.

---

## Consequences

**Positive:**
- **Runtime control.** Features can be toggled without redeployment.
- **Declarative enforcement.** `[RequireFeature]` on a controller class automatically gates all its actions. No per-action conditional logic.
- **Optional config JSON.** The `Config` column allows features to carry parameters (e.g., feed item limit, chat file size limit) without schema changes.
- **Admin API.** A single `PUT` endpoint manages all feature flags across all tenants.

**Negative:**
- **Master DB lookup per request.** The feature check requires the tenant's features to be loaded. Mitigated by caching the tenant data (including features) in `IMemoryCache` after the first resolution.
- **No UI for feature management.** Currently only available via the admin REST API or Swagger. A future admin UI panel would improve usability.
- **No feature versioning.** Feature flags are binary (on/off). Gradual rollouts (percentage-based) are not supported and would require a different system.

---

## Alternatives Rejected

**Hardcoded feature tiers (Basic/Pro/Enterprise)** were rejected because:
- Adding a new feature would require code changes to update the tier definitions.
- Per-tenant exceptions (e.g., "give tenant X the chat feature even on the Basic tier") would require special-casing.

**Environment-level feature flags** were rejected because all tenants share the same deployment; environment flags cannot distinguish between tenants.

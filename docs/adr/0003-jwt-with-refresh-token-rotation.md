# ADR-0003: JWT Access Tokens with Refresh Token Rotation

**Date:** 2026-03-09
**Status:** Accepted

---

## Context

The platform serves a Blazor Server frontend and needs a stateless, scalable authentication mechanism. We evaluated:

1. **Session cookies** — server-side session state.
2. **Long-lived JWT** — single token valid for days/weeks.
3. **Short-lived JWT + refresh token rotation** — access token expires quickly; refresh token is rotated on each use.

---

## Decision

We use **short-lived JWT access tokens (8 hours) combined with opaque refresh tokens stored in the tenant database with rotation on every use.**

- **Access token:** JWT signed with HMAC-SHA256. Contains claims: `sub`, `email`, `name`, `tenantId`, `role`, `userType`. Validated entirely by the middleware (stateless). Expires in 8 hours.
- **Refresh token:** Cryptographically random opaque string stored in the `RefreshTokens` table. On each `POST /api/auth/refresh`, the old token is revoked (`IsRevoked = true`, `ReplacedByToken` set to the new token hash) and a new pair is issued. This creates an auditable chain of token rotations.
- **Revocation:** Any refresh token can be explicitly revoked (logout). If a previously used (rotated) token is presented, the entire chain is considered compromised and all tokens for that user are revoked.
- **Cleanup:** A Hangfire job (`refresh-token-cleanup`) runs daily at 03:00 UTC and hard-deletes revoked tokens older than 7 days and expired tokens older than 30 days.

---

## Consequences

**Positive:**
- **Stateless access token validation.** The API does not query the database to validate an access token. Scales horizontally without shared session state.
- **Refresh token theft detection.** Rotation with `ReplacedByToken` allows detection of token reuse attacks (a rotated token being presented again signals a compromise).
- **Revocation at logout.** Refresh tokens can be revoked server-side even though JWTs themselves are not revocable before expiry.
- **Audit trail.** Every token rotation is recorded in the database via the `ReplacedByToken` chain.

**Negative:**
- **Access token cannot be revoked before expiry.** A stolen access token remains valid for up to 8 hours. Mitigated by the short lifetime; a future improvement could add a token blocklist (e.g., Redis) for critical security events.
- **Database write on every token refresh.** Each `POST /api/auth/refresh` requires two writes (revoke old, insert new). Acceptable at current scale.
- **Clock skew.** Distributed deployments must ensure system clocks are synchronized (NTP) to avoid premature token rejection.

---

## Alternatives Rejected

**Long-lived JWT (days/weeks)** was rejected because revocation is impossible without a blocklist, and a stolen token grants access for the entire validity period.

**Session cookies** were rejected because they require server-side session storage, complicating horizontal scaling and conflicting with the stateless API design required to serve both Blazor Server and future mobile clients.

# ADR-0005: SignalR for Real-Time Communication

**Date:** 2026-03-10
**Status:** Accepted

---

## Context

The platform requires real-time push capabilities for:
- **Chat:** new messages, typing indicators, online/offline presence.
- **Notifications:** push alerts without polling.

We evaluated:
- **Server-Sent Events (SSE)** — unidirectional server-to-client streaming.
- **WebSockets (raw)** — bidirectional, low-level.
- **SignalR** — ASP.NET Core abstraction over WebSockets/SSE/Long Polling.
- **Third-party services** (Pusher, Ably) — managed real-time infrastructure.

---

## Decision

We use **ASP.NET Core SignalR** with two typed hubs:

- **`ChatHub`** (`/hubs/chat`) — manages conversation groups, message delivery, and typing indicators.
- **`NotificationHub`** (`/hubs/notifications`) — pushes notification events to individual users.

Both hubs use **tenant-scoped group names** (`{tenantId}:user:{userId}`, `{tenantId}:conv:{conversationId}`, `{tenantId}:all`) to guarantee tenant isolation even within a shared hub instance. JWT Bearer authentication is enforced at the hub level via ASP.NET Core authorization.

Messages are sent via REST (`POST /api/chat/conversations/{id}/messages`) and the service layer uses `IHubContext<ChatHub, IChatHub>` to push the new message to the conversation group immediately after persisting to the database.

---

## Consequences

**Positive:**
- **Transport negotiation.** SignalR automatically falls back from WebSockets to SSE to Long Polling, maximising browser compatibility.
- **Group management.** SignalR's `Groups` abstraction maps cleanly onto the conversation and user group model without custom routing.
- **ASP.NET Core integration.** Authentication, middleware, and DI work identically to HTTP endpoints.
- **Typed hub interfaces.** `IChatHub` and `INotificationHub` interfaces enable compile-time safety and straightforward Moq-based testing of hub context interactions.
- **Horizontal scale path.** Adding a Redis backplane (`AddStackExchangeRedis`) enables SignalR to broadcast across multiple API instances with no code changes.

**Negative:**
- **Stateful connections.** Each connected client holds a persistent WebSocket connection. Under high concurrent user counts, connection limits and memory usage must be monitored.
- **No backplane in current setup.** Running multiple API instances without a Redis backplane will cause messages to only reach clients connected to the same instance. Documented as a known limitation for the current single-instance deployment.
- **Testing complexity.** `IHubContext<THub, TClient>` requires Moq setup in unit tests. Addressed via typed interface mocking.

---

## Alternatives Rejected

**Raw WebSockets** were rejected because they require manual connection lifecycle management, group routing, and transport fallback — all provided for free by SignalR.

**Server-Sent Events** were rejected because they are unidirectional; the chat typing indicator requires client-to-server messages.

**Third-party services (Pusher/Ably)** were rejected to avoid external service dependency, data egress costs, and to keep all tenant data within the self-hosted infrastructure.

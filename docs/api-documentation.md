# API Documentation

**Base URL:** `http://localhost:5000` (development) / your production URL
**API Version:** v1
**Format:** JSON (`Content-Type: application/json`)

---

## Table of Contents

- [Authentication](#authentication)
- [Common Headers](#common-headers)
- [Rate Limiting](#rate-limiting)
- [Error Responses](#error-responses)
- [Endpoints](#endpoints)
  - [Auth](#auth)
  - [Users](#users)
  - [Feed](#feed)
  - [Chat](#chat)
  - [Notifications](#notifications)
  - [Admin](#admin)
  - [Diagnostics](#diagnostics)
- [SignalR Hubs](#signalr-hubs)
  - [ChatHub](#chathub)
  - [NotificationHub](#notificationhub)
- [Health Checks](#health-checks)

---

## Authentication

The API uses **JWT Bearer** tokens. Include the token in the `Authorization` header:

```
Authorization: Bearer <access_token>
```

Tokens are obtained via `POST /api/auth/login` and expire after **8 hours**.
Use `POST /api/auth/refresh` to obtain a new pair before expiry.

---

## Common Headers

| Header | Required | Description |
|---|---|---|
| `X-Tenant-Id` | Yes (all tenant endpoints) | Tenant identifier (e.g. `tenant001`) |
| `Authorization` | Yes (protected endpoints) | `Bearer <access_token>` |
| `Content-Type` | Yes (POST/PUT) | `application/json` |

---

## Rate Limiting

| Policy | Applies to | Limit |
|---|---|---|
| `auth` | All `/api/auth/*` endpoints | 10 requests / minute per IP |
| `api` | Feed, Chat endpoints | 200 requests / minute per IP |

Exceeding the limit returns `429 Too Many Requests`.

---

## Error Responses

All errors follow a consistent structure:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Descriptive error message",
  "traceId": "00-abc123..."
}
```

| Status | Meaning |
|---|---|
| `400` | Validation error or bad input |
| `401` | Missing or invalid JWT |
| `403` | Insufficient role / feature disabled for tenant |
| `404` | Resource not found |
| `409` | Conflict (e.g. duplicate email) |
| `429` | Rate limit exceeded |
| `500` | Internal server error |

---

## Endpoints

---

### Auth

All auth endpoints require `X-Tenant-Id` and are rate-limited to **10 req/min**.

---

#### `POST /api/auth/register`

Register a new user account.

**Request body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePass1!",
  "firstName": "Mario",
  "lastName": "Rossi",
  "userType": 1
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `email` | string | Yes | Must be a valid email |
| `password` | string | Yes | Min 8 chars, 1 uppercase, 1 digit |
| `firstName` | string | Yes | — |
| `lastName` | string | Yes | — |
| `userType` | integer | No | `0` = Internal, `1` = External (default) |

**Responses:**

| Status | Description |
|---|---|
| `204 No Content` | Registration successful; check your email to confirm |
| `400 Bad Request` | Validation error |
| `409 Conflict` | Email already registered |

---

#### `POST /api/auth/login`

Authenticate and receive tokens.

**Request body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePass1!"
}
```

**Response `200 OK`:**
```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "dGhpcyBpcyBh...",
  "expiresIn": 28800
}
```

| Status | Description |
|---|---|
| `200 OK` | Login successful |
| `401 Unauthorized` | Invalid credentials or unconfirmed email |

---

#### `POST /api/auth/refresh`

Exchange a refresh token for a new access/refresh token pair (token rotation).

**Request body:**
```json
{
  "refreshToken": "dGhpcyBpcyBh..."
}
```

**Response `200 OK`:**
```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "bmV3UmVmcmVzaA...",
  "expiresIn": 28800
}
```

| Status | Description |
|---|---|
| `200 OK` | New tokens issued |
| `401 Unauthorized` | Token expired, revoked, or not found |

---

#### `GET /api/auth/confirm-email`

Confirm a user's email address from the link sent after registration.

**Query parameters:**

| Parameter | Required | Description |
|---|---|---|
| `userId` | Yes | User ID from the email link |
| `token` | Yes | Confirmation token from the email link |

| Status | Description |
|---|---|
| `204 No Content` | Email confirmed |
| `400 Bad Request` | Invalid or expired token |

---

#### `POST /api/auth/forgot-password`

Request a password reset email.

> Anti-enumeration: always returns `204` regardless of whether the email exists.

**Request body:**
```json
{
  "email": "user@example.com"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Always returned |

---

#### `POST /api/auth/reset-password`

Set a new password using the token from the reset email.

**Request body:**
```json
{
  "userId": 42,
  "token": "CfDJ8Ny...",
  "newPassword": "NewSecurePass1!"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Password reset successfully |
| `400 Bad Request` | Invalid or expired token |

---

#### `POST /api/auth/logout`

Revoke the current refresh token.

**Authorization:** Required

**Request body:**
```json
{
  "refreshToken": "dGhpcyBpcyBh..."
}
```

| Status | Description |
|---|---|
| `204 No Content` | Token revoked |
| `401 Unauthorized` | Not authenticated |

---

### Users

**Authorization:** Required on all endpoints.

---

#### `GET /api/users`

Get a paginated list of users in the current tenant.

**Query parameters:**

| Parameter | Default | Description |
|---|---|---|
| `page` | `1` | Page number |
| `pageSize` | `20` | Items per page (max 100) |
| `search` | — | Filter by name or email |

**Response `200 OK`:**
```json
{
  "items": [
    {
      "id": 1,
      "email": "admin@example.com",
      "firstName": "Admin",
      "lastName": "User",
      "userType": 0,
      "isActive": true,
      "avatarUrl": null,
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

---

#### `GET /api/users/{id}`

Get a specific user's public profile.

**Response `200 OK`:**
```json
{
  "id": 1,
  "email": "user@example.com",
  "firstName": "Mario",
  "lastName": "Rossi",
  "userType": 1,
  "isActive": true,
  "avatarUrl": "https://example.com/avatar.png",
  "createdAt": "2026-01-01T00:00:00Z"
}
```

| Status | Description |
|---|---|
| `200 OK` | User found |
| `404 Not Found` | User does not exist |

---

#### `GET /api/users/me`

Get the currently authenticated user's profile.

**Response `200 OK`:** Same structure as `GET /api/users/{id}`.

---

#### `PUT /api/users/me`

Update the current user's profile.

**Request body:**
```json
{
  "firstName": "Mario",
  "lastName": "Rossi",
  "avatarUrl": "https://example.com/avatar.png"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Updated successfully |
| `400 Bad Request` | Validation error |

---

#### `PUT /api/users/me/password`

Change the current user's password.

**Request body:**
```json
{
  "currentPassword": "OldPass1!",
  "newPassword": "NewPass1!"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Password changed |
| `400 Bad Request` | Current password wrong or new password invalid |

---

#### `POST /api/users/{id}/make-admin`

Promote a user to the Admin role.

**Authorization:** Admin role required.

| Status | Description |
|---|---|
| `204 No Content` | Role assigned |
| `403 Forbidden` | Not an Admin |
| `404 Not Found` | User not found |

---

### Feed

**Authorization:** Required.
**Feature flag:** `social:feed` must be enabled for the tenant.
**Rate limit:** `api` policy (200 req/min).

---

#### `GET /api/feed`

Get the paginated activity feed for the current user.

**Query parameters:**

| Parameter | Default | Description |
|---|---|---|
| `page` | `1` | Page number |
| `pageSize` | `20` | Items per page |

**Response `200 OK`:**
```json
{
  "items": [
    {
      "id": 10,
      "userId": 3,
      "eventType": "post:create",
      "entityId": "55",
      "entityType": "Post",
      "payload": "{\"title\":\"Hello world\"}",
      "isPublic": true,
      "reactionCount": 2,
      "userReaction": "like",
      "createdAt": "2026-03-20T10:00:00Z"
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

---

#### `POST /api/feed/publish`

Publish a new activity event to the feed.

**Request body:**
```json
{
  "eventType": "post:create",
  "entityId": "55",
  "entityType": "Post",
  "payload": "{\"title\":\"Hello world\"}",
  "isPublic": true
}
```

| Status | Description |
|---|---|
| `201 Created` | Event published |
| `400 Bad Request` | Validation error |
| `403 Forbidden` | Feature disabled |

---

#### `POST /api/feed/{id}/react`

Add or toggle a reaction on an activity event.

**Request body:**
```json
{
  "reactionType": "like"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Reaction saved / toggled |
| `404 Not Found` | Event not found |

---

#### `POST /api/feed/follow/{followedId}`

Follow another user.

| Status | Description |
|---|---|
| `204 No Content` | Now following |
| `400 Bad Request` | Cannot follow yourself |
| `409 Conflict` | Already following |

---

#### `DELETE /api/feed/follow/{followedId}`

Unfollow a user.

| Status | Description |
|---|---|
| `204 No Content` | Unfollowed |
| `404 Not Found` | Follow relationship not found |

---

### Chat

**Authorization:** Required.
**Feature flag:** `social:chat` must be enabled for the tenant.
**Rate limit:** `api` policy (200 req/min).

---

#### `GET /api/chat/conversations`

Get the current user's conversations.

**Query parameters:**

| Parameter | Default | Description |
|---|---|---|
| `page` | `1` | Page number |
| `pageSize` | `20` | Items per page |

**Response `200 OK`:**
```json
{
  "items": [
    {
      "id": 1,
      "title": null,
      "isGroup": false,
      "lastMessageAt": "2026-03-20T09:00:00Z",
      "unreadCount": 3,
      "participants": [
        { "userId": 1, "role": 0 },
        { "userId": 2, "role": 0 }
      ]
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

---

#### `POST /api/chat/conversations/direct/{targetUserId}`

Get an existing 1:1 conversation with the target user or create one.

**Response `200 OK`:**
```json
{
  "id": 1,
  "isGroup": false,
  "participants": [...]
}
```

---

#### `POST /api/chat/conversations/group`

Create a new group conversation.

**Request body:**
```json
{
  "title": "Project Alpha",
  "participantIds": [2, 3, 4]
}
```

**Response `201 Created`:**
```json
{
  "id": 5,
  "title": "Project Alpha",
  "isGroup": true
}
```

---

#### `GET /api/chat/conversations/{conversationId}/messages`

Get messages in a conversation (newest first).

**Query parameters:**

| Parameter | Default | Description |
|---|---|---|
| `page` | `1` | Page number |
| `pageSize` | `50` | Items per page |

**Response `200 OK`:**
```json
{
  "items": [
    {
      "id": 101,
      "senderId": 2,
      "body": "Hello!",
      "attachmentUrl": null,
      "sentAt": "2026-03-20T09:00:00Z",
      "readBy": [1, 2]
    }
  ],
  "totalCount": 101,
  "page": 1,
  "pageSize": 50
}
```

---

#### `POST /api/chat/conversations/{conversationId}/messages`

Send a message to a conversation.

**Request body:**
```json
{
  "body": "Hello!",
  "attachmentUrl": null
}
```

**Response `201 Created`:**
```json
{
  "id": 102,
  "senderId": 1,
  "body": "Hello!",
  "sentAt": "2026-03-20T10:05:00Z"
}
```

The message is also pushed in real time to all conversation participants via the **ChatHub**.

---

#### `PUT /api/chat/conversations/{conversationId}/read`

Mark all messages in a conversation as read by the current user.

| Status | Description |
|---|---|
| `204 No Content` | Marked as read |
| `403 Forbidden` | Not a participant |

---

### Notifications

**Authorization:** Required.
**Feature flag:** `social:notifications` must be enabled for the tenant.

---

#### `GET /api/notifications`

Get unread notifications for the current user.

**Response `200 OK`:**
```json
[
  {
    "id": 1,
    "type": "follow",
    "title": "New follower",
    "message": "Mario Rossi started following you.",
    "entityId": "3",
    "entityType": "User",
    "isRead": false,
    "createdAt": "2026-03-20T08:00:00Z"
  }
]
```

---

#### `PUT /api/notifications/{id}/read`

Mark a single notification as read.

| Status | Description |
|---|---|
| `204 No Content` | Marked as read |
| `404 Not Found` | Notification not found |

---

#### `PUT /api/notifications/read-all`

Mark all notifications as read for the current user.

| Status | Description |
|---|---|
| `204 No Content` | All notifications marked as read |

---

### Admin

**Authorization:** Admin role required on all endpoints.
**No `X-Tenant-Id` header required** for global endpoints (stats, audit logs, tenant list).
Tenant-scoped endpoints operate on the specified `{tenantId}` directly.

---

#### `GET /api/admin/stats`

Get global platform statistics.

**Response `200 OK`:**
```json
{
  "totalTenants": 3,
  "activeTenants": 2,
  "pendingMigrations": 1,
  "failedMigrations": 0
}
```

---

#### `GET /api/admin/tenants`

List all tenants with connection info and features.

**Response `200 OK`:**
```json
[
  {
    "id": 1,
    "tenantId": "tenant001",
    "subdomain": "tenant001",
    "name": "Demo Tenant",
    "isActive": true,
    "createdAt": "2026-01-01T00:00:00Z",
    "features": [
      { "featureKey": "social:feed", "isEnabled": true },
      { "featureKey": "social:chat", "isEnabled": true }
    ]
  }
]
```

---

#### `GET /api/admin/tenants/{tenantId}`

Get full tenant details including migration history.

---

#### `POST /api/admin/tenants`

Create a new tenant and provision its database.

**Request body:**
```json
{
  "tenantId": "acme-corp",
  "subdomain": "acme",
  "name": "Acme Corporation",
  "connectionString": "Host=postgres;Database=mysolutionhub_acme;...",
  "region": "eu-west",
  "features": ["social:feed", "social:chat"]
}
```

**Response `201 Created`:**
```json
{
  "tenantId": "acme-corp",
  "name": "Acme Corporation",
  "isActive": false
}
```

> Tenant starts as inactive. Database provisioning runs as a Hangfire background job. Activate the tenant once provisioning completes.

---

#### `POST /api/admin/tenants/{tenantId}/provision`

Re-trigger database provisioning for a failed tenant.

| Status | Description |
|---|---|
| `202 Accepted` | Provisioning job enqueued |
| `404 Not Found` | Tenant not found |

---

#### `PUT /api/admin/tenants/{tenantId}`

Update tenant metadata.

**Request body:**
```json
{
  "name": "Acme Corp Updated",
  "subdomain": "acme-new",
  "isActive": true
}
```

| Status | Description |
|---|---|
| `204 No Content` | Updated |
| `409 Conflict` | Subdomain already in use |

---

#### `PUT /api/admin/tenants/{tenantId}/features/{featureKey}`

Enable or disable a feature for a tenant.

**Request body:**
```json
{
  "isEnabled": true,
  "config": "{}"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Feature updated |
| `404 Not Found` | Tenant or feature not found |

---

#### `GET /api/admin/tenants/{tenantId}/users`

List all users in a specific tenant (cross-tenant admin query).

**Query parameters:** `page`, `pageSize`, `search`

---

#### `PUT /api/admin/tenants/{tenantId}/users/{userId}/toggle`

Activate or deactivate a user in a specific tenant.

| Status | Description |
|---|---|
| `204 No Content` | User status toggled |
| `404 Not Found` | User not found |

---

#### `GET /api/admin/tenants/{tenantId}/settings`

Get all settings for a tenant.

**Response `200 OK`:**
```json
[
  { "key": "theme", "value": "dark", "updatedAt": "2026-03-01T00:00:00Z" },
  { "key": "language", "value": "it", "updatedAt": "2026-03-01T00:00:00Z" }
]
```

---

#### `PUT /api/admin/tenants/{tenantId}/settings/{key}`

Create or update a setting for a tenant.

**Request body:**
```json
{
  "value": "dark"
}
```

| Status | Description |
|---|---|
| `204 No Content` | Setting saved (upsert) |

---

#### `DELETE /api/admin/tenants/{tenantId}/settings/{key}`

Delete a setting for a tenant.

| Status | Description |
|---|---|
| `204 No Content` | Setting deleted |
| `404 Not Found` | Setting not found |

---

#### `GET /api/admin/audit-logs`

Get paginated audit logs.

**Query parameters:**

| Parameter | Default | Description |
|---|---|---|
| `tenantId` | — | Filter by tenant (omit for global logs) |
| `page` | `1` | Page number |
| `pageSize` | `50` | Items per page |

**Response `200 OK`:**
```json
{
  "items": [
    {
      "id": 1,
      "tenantId": "tenant001",
      "actorId": "1",
      "actorName": "Admin User",
      "action": "tenant:provision",
      "entityType": "Tenant",
      "entityId": "tenant001",
      "changes": null,
      "ipAddress": "127.0.0.1",
      "timestamp": "2026-03-20T08:00:00Z"
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 50
}
```

---

#### `GET /api/admin/tenants/{tenantId}/migrations`

Get migration history for a tenant database.

**Response `200 OK`:**
```json
[
  {
    "migrationId": "20260309165859_InitialTenantDb",
    "appliedAt": "2026-03-09T16:58:59Z",
    "status": 1,
    "errorMessage": null
  }
]
```

`status`: `0` = Pending, `1` = Completed, `2` = Failed

---

### Diagnostics

**Authorization:** Admin role required.

---

#### `GET /api/diagnostics/tenant`

Returns the resolved `TenantContext` for the current request (useful for debugging multi-tenancy).

**Response `200 OK`:**
```json
{
  "tenantId": "tenant001",
  "connectionString": "[REDACTED]",
  "isResolved": true
}
```

---

#### `GET /api/diagnostics/master-db`

Returns master database configuration status.

---

## SignalR Hubs

Connect using the [SignalR JavaScript client](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client) or the [.NET client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client).

Both hubs require:
- **JWT Bearer token** passed as `access_token` query parameter or `Authorization` header.
- **`X-Tenant-Id`** header for tenant isolation.

---

### ChatHub

**URL:** `/hubs/chat`

#### Connection example (JavaScript)

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/chat", {
    accessTokenFactory: () => localStorage.getItem("accessToken")
  })
  .withAutomaticReconnect()
  .build();

await connection.start();
```

#### Server → Client events

| Event | Payload | Description |
|---|---|---|
| `UserOnline` | `{ userId: int }` | A user came online |
| `UserOffline` | `{ userId: int }` | A user went offline |
| `UserTyping` | `{ conversationId: int, userId: int, isTyping: bool }` | Typing indicator |
| `NewMessage` | `{ messageId, conversationId, senderId, body, sentAt }` | New message received |

#### Client → Server methods

| Method | Parameters | Description |
|---|---|---|
| `JoinConversation` | `conversationId: int` | Subscribe to messages in a conversation |
| `LeaveConversation` | `conversationId: int` | Unsubscribe from a conversation |
| `SendTyping` | `conversationId: int, isTyping: bool` | Broadcast typing indicator to participants |

---

### NotificationHub

**URL:** `/hubs/notifications`

#### Connection example (JavaScript)

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/notifications", {
    accessTokenFactory: () => localStorage.getItem("accessToken")
  })
  .withAutomaticReconnect()
  .build();

connection.on("NewNotification", (notification) => {
  console.log("New notification:", notification);
});

await connection.start();
```

#### Server → Client events

| Event | Payload | Description |
|---|---|---|
| `NewNotification` | `{ id, type, title, message, entityId, entityType, createdAt }` | New notification pushed to user |
| `NotificationRead` | `{ notificationId: int }` | Confirmation that a notification was marked read |

#### Client → Server methods

| Method | Parameters | Description |
|---|---|---|
| `MarkAsRead` | `notificationId: int` | Mark a notification as read |

---

## Health Checks

| Endpoint | Auth | Description |
|---|---|---|
| `GET /health` | No | Liveness: returns `200 Healthy` if app is up |
| `GET /health/ready` | No | Readiness: verifies master DB and one tenant DB are reachable |

**Response `200 OK`:**
```json
{
  "status": "Healthy",
  "results": {
    "master-db": { "status": "Healthy", "duration": "00:00:00.015" },
    "tenant-db": { "status": "Healthy", "duration": "00:00:00.021" }
  }
}
```

**Response `503 Service Unavailable`** if any check fails.

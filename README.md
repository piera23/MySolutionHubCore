# MySolutionHubCore

Multi-tenant SaaS platform built with **ASP.NET Core 8**, **Blazor Server**, **Entity Framework Core 8**, **PostgreSQL**, **SignalR** and **Hangfire**.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Prerequisites](#prerequisites)
- [Quick Start with Docker](#quick-start-with-docker)
- [Local Development (without Docker)](#local-development-without-docker)
- [Configuration Reference](#configuration-reference)
- [Project Structure](#project-structure)
- [Multi-Tenancy Model](#multi-tenancy-model)
- [Authentication & Authorization](#authentication--authorization)
- [Feature Flags](#feature-flags)
- [Background Jobs](#background-jobs)
- [Health Checks](#health-checks)
- [Running Tests](#running-tests)
- [Database Scripts](#database-scripts)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Client (Browser)                      │
└────────────────────────┬────────────────────────────────┘
                         │ HTTP / WebSocket
           ┌─────────────┴─────────────┐
           │                           │
    ┌──────▼──────┐            ┌───────▼──────┐
    │  Web (Blazor │            │  API (ASP.NET │
    │   Server)   │◄──────────►│   Core 8)    │
    │  :5001      │  HTTP      │  :5000       │
    └─────────────┘            └──────┬───────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    │                 │                  │
             ┌──────▼──────┐  ┌──────▼──────┐  ┌──────▼──────┐
             │ Master DB   │  │ Tenant DB 1 │  │ Tenant DB N │
             │ (registry)  │  │ (app data)  │  │ (app data)  │
             └─────────────┘  └─────────────┘  └─────────────┘
```

**Layer breakdown:**

| Layer | Project | Responsibility |
|---|---|---|
| Presentation | `Api` | REST endpoints, rate limiting, health checks |
| Presentation | `Web` | Blazor Server UI, SignalR client |
| Application | `Application` | Service interfaces, DTOs, use cases |
| Domain | `Domain` | Entities, domain contracts |
| Infrastructure | `Infrastructure` | EF Core, Identity, SignalR hubs, services |
| Data | `MasterDb` | Master database context and migrations |
| Tests | `Tests` | xUnit unit tests |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the containerized setup)
- [PostgreSQL 16+](https://www.postgresql.org/) (for local development without Docker)

---

## Quick Start with Docker

```bash
git clone https://github.com/piera23/MySolutionHubCore.git
cd MySolutionHubCore
docker compose up --build
```

Services available after startup:

| Service | URL | Credentials |
|---|---|---|
| API | http://localhost:5000 | — |
| API Swagger | http://localhost:5000/swagger | — |
| Blazor Web | http://localhost:5001 | — |
| pgAdmin | http://localhost:5050 | admin@dev.local / admin |
| API Health | http://localhost:5000/health | — |

> **pgAdmin** is only started with the `dev-tools` Docker Compose profile:
> ```bash
> docker compose --profile dev-tools up --build
> ```

The first boot automatically:
1. Applies EF Core migrations on the master database
2. Seeds the development tenant (`tenant001`) and its database
3. Seeds the three default roles: `Admin`, `Internal`, `External`

---

## Local Development (without Docker)

### 1. Start PostgreSQL

```bash
# Using Docker just for the database
docker run -d \
  --name pg-dev \
  -e POSTGRES_PASSWORD=postgres_dev \
  -p 5432:5432 \
  postgres:16-alpine
```

### 2. Create databases

```bash
psql -U postgres -h localhost -c "CREATE DATABASE mysolutionhub_master;"
psql -U postgres -h localhost -c "CREATE DATABASE mysolutionhub_tenant001;"
```

### 3. Configure secrets

Use [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) to avoid committing credentials:

```bash
cd Api
dotnet user-secrets set "ConnectionStrings:MasterDb" "Host=localhost;Port=5432;Database=mysolutionhub_master;Username=postgres;Password=postgres_dev"
dotnet user-secrets set "MultiTenant:EncryptionKey" "chiave-sviluppo-locale-1234567890"
dotnet user-secrets set "Jwt:Key" "chiave-segreta-jwt-sviluppo-minimo-32-caratteri!!"
```

### 4. Run the API

```bash
cd Api
dotnet run
# API available at http://localhost:5000
```

### 5. Run the Blazor frontend

```bash
cd Web
dotnet run
# Web available at http://localhost:5001
```

---

## Configuration Reference

All sensitive values must be supplied via environment variables or User Secrets. The application **will not start** if any required key is missing or still contains a `REPLACE_` placeholder.

| Key | Required | Description |
|---|---|---|
| `ConnectionStrings:MasterDb` | Yes | PostgreSQL connection string for master database |
| `MultiTenant:EncryptionKey` | Yes | AES-256 key used to encrypt tenant connection strings |
| `Jwt:Key` | Yes | HMAC-SHA256 signing key (min 32 chars) |
| `Jwt:Issuer` | Yes | JWT issuer claim |
| `Jwt:Audience` | Yes | JWT audience claim |
| `App:BaseUrl` | Yes | Public base URL (used in email links) |
| `Cors:AllowedOrigins` | No | JSON array of allowed CORS origins (default: localhost:5001) |

### Docker environment variable overrides

All values in `docker-compose.yml` are overridable via shell environment:

```bash
JWT_KEY=my-super-secret-key docker compose up
```

---

## Project Structure

```
MySolutionHubCore/
├── Api/                    # REST API (ASP.NET Core 8)
│   ├── Controllers/        # HTTP endpoints
│   ├── Hangfire/           # Hangfire auth filter
│   ├── HealthChecks/       # MasterDb and TenantDb health checks
│   ├── Middleware/         # Global exception handler
│   └── Seed/               # Development data seeders
├── Application/            # Use-case interfaces and DTOs
├── Domain/                 # Entities and domain contracts
├── Infrastructure/         # EF Core, Identity, SignalR, services
│   ├── Hubs/               # ChatHub, NotificationHub
│   ├── Identity/           # AuthService, JwtService, ApplicationUser
│   ├── MultiTenant/        # Tenant resolution and encryption
│   ├── Persistence/        # BaseAppDbContext and migrations
│   └── Services/           # Business services
├── MasterDb/               # Master database context and migrations
├── Web/                    # Blazor Server frontend
│   ├── Components/Pages/   # Blazor pages
│   └── Services/           # API client, auth state, SignalR client
├── Tests/                  # xUnit unit tests
├── database/
│   └── scripts/            # Raw PostgreSQL DDL scripts
│       ├── 01_master_db.sql
│       └── 02_tenant_db.sql
├── docker/
│   └── postgres/init.sql   # Database initialization script
├── docker-compose.yml
├── Api/Dockerfile
└── Web/Dockerfile
```

---

## Multi-Tenancy Model

The platform uses a **database-per-tenant** strategy:

- **Master database** (`mysolutionhub_master`) stores the tenant registry, encrypted connection strings, feature flags, settings, and audit logs.
- **Tenant databases** (`mysolutionhub_<tenant_id>`) each contain the full application schema: users, social feed, chat, notifications.

Tenant resolution happens per request via the `TenantResolutionMiddleware`:
1. Reads the `X-Tenant-Id` HTTP header.
2. Looks up the tenant in the master DB (with in-memory caching).
3. Decrypts the connection string and injects it into `TenantContext`.
4. All subsequent EF Core queries use the tenant-specific connection.

```
Request → TenantResolutionMiddleware
              │
              ▼
     Read X-Tenant-Id header
              │
              ▼
     Lookup in MasterDb (cached)
              │
              ▼
     Decrypt connection string
              │
              ▼
     Inject into TenantContext
              │
              ▼
     Controller / Service (uses TenantDbContext)
```

---

## Authentication & Authorization

### Flow

1. `POST /api/auth/register` — Creates user account; returns 204 (email confirm required).
2. `POST /api/auth/login` — Returns `accessToken` (JWT, 8 h) and `refreshToken`.
3. `POST /api/auth/refresh` — Rotates the refresh token; returns new pair.
4. `POST /api/auth/logout` — Revokes the current refresh token.

### JWT claims

| Claim | Value |
|---|---|
| `sub` / `nameidentifier` | User ID |
| `email` | User email |
| `name` | Full name |
| `tenantId` | Current tenant identifier |
| `role` | Role name(s) |
| `userType` | `Internal` or `External` |

### Password reset

1. `POST /api/auth/forgot-password` — Always returns 204 (anti-enumeration). If the email exists, a reset link is sent.
2. `POST /api/auth/reset-password` — Validates token and sets new password.

### Roles

| Role | Description |
|---|---|
| `Admin` | Full access to admin endpoints and Hangfire dashboard |
| `Internal` | Internal company users |
| `External` | External / client users |

### Authorization policies

| Policy | Requirement |
|---|---|
| `InternalOnly` | Claim `userType = Internal` |
| `ExternalOnly` | Claim `userType = External` |

---

## Feature Flags

Each tenant can independently enable or disable platform features. Features are stored in `TenantFeatures` and checked on every request via the `[RequireFeature]` attribute.

| Feature key | Protects |
|---|---|
| `social:feed` | Feed endpoints and activity events |
| `social:chat` | Chat endpoints and ChatHub |
| `social:notifications` | Notifications endpoints |

Manage features via the admin API:

```
PUT /api/admin/tenants/{tenantId}/features/{featureKey}
Body: { "isEnabled": true }
```

---

## Background Jobs

Hangfire is used for all background processing. The Hangfire dashboard is available in production at `/hangfire` (Admin role required).

| Job ID | Schedule | Description |
|---|---|---|
| `tenant-migrations` | Every 12 h | Checks and applies pending EF migrations on all tenant databases |
| `refresh-token-cleanup` | Daily at 03:00 UTC | Deletes revoked tokens older than 7 days and expired tokens older than 30 days |

Both jobs expose a `TriggerManualRunAsync` method that can be called from the Hangfire dashboard.

---

## Health Checks

| Endpoint | Tag | Description |
|---|---|---|
| `GET /health` | — | Liveness check (always 200 if app is running) |
| `GET /health/ready` | `ready` | Readiness: checks master DB and first active tenant DB connectivity |

The Docker Compose `api` service uses `GET /health` as its health check to gate the `web` service startup.

---

## Running Tests

```bash
cd Tests
dotnet test
```

Test coverage:

| File | What is tested |
|---|---|
| `JwtServiceTests.cs` | Token generation, claims, algorithm, expiry, validation |
| `ActivityServiceTests.cs` | Feed events, reactions, follow/unfollow |
| `ChatServiceTests.cs` | Conversations, messages, participants |
| `AuditServiceTests.cs` | Audit log creation and retrieval |
| `LoggingEmailServiceTests.cs` | Email logging output |
| `TenantContextTests.cs` | Tenant context resolution |
| `TenantEncryptionTests.cs` | AES-256 encrypt/decrypt roundtrip |
| `TenantResolverTests.cs` | Header-based tenant resolution |

> **Note:** Tests that rely on `ExecuteUpdateAsync` (bulk updates) are skipped with EF InMemory and require a real PostgreSQL integration test environment.

---

## Database Scripts

Raw PostgreSQL DDL scripts are available in `database/scripts/` for manual setup or reference:

| Script | Database | Description |
|---|---|---|
| `01_master_db.sql` | `mysolutionhub_master` | All master database tables, indexes, and constraints |
| `02_tenant_db.sql` | `mysolutionhub_<tenant_id>` | All tenant database tables with identity, social, chat, and notifications |

All scripts are **idempotent** (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`) and safe to re-run.

---

## License

This project is proprietary. All rights reserved.

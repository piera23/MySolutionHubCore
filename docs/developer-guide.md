# Developer Guide

This guide covers the day-to-day development tasks for MySolutionHubCore: adding tenants, creating EF Core migrations, adding new API endpoints, writing tests, and deploying.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [First-Time Setup](#first-time-setup)
- [Daily Development Workflow](#daily-development-workflow)
- [Adding a New Tenant](#adding-a-new-tenant)
- [EF Core Migrations](#ef-core-migrations)
  - [Tenant Database Migrations](#tenant-database-migrations)
  - [Master Database Migrations](#master-database-migrations)
- [Adding a New API Endpoint](#adding-a-new-api-endpoint)
- [Adding a New Feature Flag](#adding-a-new-feature-flag)
- [Adding a New Background Job](#adding-a-new-background-job)
- [Adding a New SignalR Event](#adding-a-new-signalr-event)
- [Writing Unit Tests](#writing-unit-tests)
- [Configuration & Secrets](#configuration--secrets)
- [Docker & Deployment](#docker--deployment)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop |
| EF Core tools | 8.0+ | `dotnet tool install -g dotnet-ef` |
| Git | — | https://git-scm.com |

Verify your setup:

```bash
dotnet --version   # 8.x.x
dotnet ef --version  # 8.x.x
docker --version
```

---

## First-Time Setup

### Option A: Docker (recommended)

```bash
git clone https://github.com/piera23/MySolutionHubCore.git
cd MySolutionHubCore
docker compose up --build
```

Everything is ready. API at `http://localhost:5000`, Web at `http://localhost:5001`.

### Option B: Local (without Docker)

1. **Start PostgreSQL:**
   ```bash
   docker run -d --name pg-dev -e POSTGRES_PASSWORD=postgres_dev -p 5432:5432 postgres:16-alpine
   ```

2. **Create databases:**
   ```bash
   psql -U postgres -h localhost -c "CREATE DATABASE mysolutionhub_master;"
   psql -U postgres -h localhost -c "CREATE DATABASE mysolutionhub_tenant001;"
   ```

3. **Configure User Secrets for the API:**
   ```bash
   cd Api
   dotnet user-secrets set "ConnectionStrings:MasterDb" \
     "Host=localhost;Port=5432;Database=mysolutionhub_master;Username=postgres;Password=postgres_dev"
   dotnet user-secrets set "MultiTenant:EncryptionKey" "chiave-sviluppo-locale-1234567890"
   dotnet user-secrets set "Jwt:Key" "chiave-segreta-jwt-sviluppo-minimo-32-caratteri!!"
   dotnet user-secrets set "Jwt:Issuer" "MySolutionHub"
   dotnet user-secrets set "Jwt:Audience" "MySolutionHub.Clients"
   dotnet user-secrets set "App:BaseUrl" "http://localhost:5001"
   ```

4. **Run the API** (applies migrations and seeds development data on first run):
   ```bash
   cd Api && dotnet run
   ```

5. **Run the Web frontend:**
   ```bash
   cd Web && dotnet run
   ```

---

## Daily Development Workflow

```bash
# Start only the database (if developing locally)
docker compose up postgres -d

# Run the API (terminal 1)
cd Api && dotnet run

# Run the Web frontend (terminal 2)
cd Web && dotnet run

# Run tests (terminal 3)
cd Tests && dotnet test --watch
```

The API auto-reloads on file changes when run with `dotnet watch run`.

---

## Adding a New Tenant

### Via the Admin API

Use `POST /api/admin/tenants` with an Admin JWT:

```bash
curl -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer <admin_token>" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "acme-corp",
    "subdomain": "acme",
    "name": "Acme Corporation",
    "connectionString": "Host=postgres;Port=5432;Database=mysolutionhub_acme;Username=postgres;Password=postgres_dev",
    "region": "eu-west",
    "features": ["social:feed", "social:chat"]
  }'
```

This:
1. Inserts the tenant record in the master DB.
2. Encrypts and stores the connection string.
3. Enqueues a Hangfire job to create the tenant database and run migrations.
4. Returns immediately with the tenant in `isActive: false` state.

**Activate the tenant** once provisioning completes:

```bash
curl -X PUT http://localhost:5000/api/admin/tenants/acme-corp \
  -H "Authorization: Bearer <admin_token>" \
  -H "Content-Type: application/json" \
  -d '{ "isActive": true }'
```

### Via the development seeder

For local development, add entries to `Api/Seed/MasterDbSeeder.cs`. The seeder runs automatically on startup in the Development environment.

---

## EF Core Migrations

### Tenant Database Migrations

The tenant database context is `BaseAppDbContext` in the `Infrastructure` project. The design-time factory is `BaseAppDbContextFactory`, which reads the connection string from the `TENANT_DB_CS` environment variable.

**Create a new migration:**

```bash
# From the solution root
export TENANT_DB_CS="Host=localhost;Port=5432;Database=mysolutionhub_tenant001;Username=postgres;Password=postgres_dev"

dotnet ef migrations add <MigrationName> \
  --project Infrastructure \
  --startup-project Api \
  --context BaseAppDbContext
```

**Apply migrations manually:**

```bash
dotnet ef database update \
  --project Infrastructure \
  --startup-project Api \
  --context BaseAppDbContext \
  --connection "Host=localhost;Port=5432;Database=mysolutionhub_tenant001;Username=postgres;Password=postgres_dev"
```

In production, migrations are applied automatically by the `TenantMigrationHostedService` Hangfire job.

> **Important:** After creating a migration, verify that the generated migration file uses `NpgsqlValueGenerationStrategy.IdentityByDefaultColumn` (not `Sqlite:Autoincrement` or SQL Server annotations). Add `using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;` to the migration file if needed.

### Master Database Migrations

The master database context is `MasterDbContext` in the `MasterDb` project.

```bash
export MASTERDB_CS="Host=localhost;Port=5432;Database=mysolutionhub_master;Username=postgres;Password=postgres_dev"

dotnet ef migrations add <MigrationName> \
  --project MasterDb \
  --startup-project Api \
  --context MasterDbContext

dotnet ef database update \
  --project MasterDb \
  --startup-project Api \
  --context MasterDbContext \
  --connection "Host=localhost;Port=5432;Database=mysolutionhub_master;Username=postgres;Password=postgres_dev"
```

The master database migrations are applied automatically in the Development environment on startup (`await masterDb.Database.MigrateAsync()` in `Program.cs`).

---

## Adding a New API Endpoint

### 1. Define the interface (Application layer)

If the endpoint requires a new service method, add it to the relevant interface in `Application/Interfaces/`:

```csharp
// Application/Interfaces/IMyService.cs
public interface IMyService
{
    Task<MyDto> GetSomethingAsync(int id, CancellationToken ct = default);
}
```

### 2. Implement the service (Infrastructure layer)

```csharp
// Infrastructure/Services/MyService.cs
public class MyService : IMyService
{
    private readonly BaseAppDbContext _db;

    public MyService(BaseAppDbContext db) => _db = db;

    public async Task<MyDto> GetSomethingAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.MyEntities.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Entity {id} not found");
        return new MyDto(entity.Id, entity.Name);
    }
}
```

### 3. Register the service

Add the registration in `Infrastructure/InfrastructureServiceExtensions.cs`:

```csharp
services.AddScoped<IMyService, MyService>();
```

### 4. Add the controller

```csharp
// Api/Controllers/MyController.cs
[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("api")]
public class MyController : ControllerBase
{
    private readonly IMyService _service;

    public MyController(IMyService service) => _service = service;

    [HttpGet("{id}")]
    [RequireFeature("my:feature")]   // optional: gate behind a feature flag
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await _service.GetSomethingAsync(id, ct);
        return Ok(result);
    }
}
```

### 5. Write a test

See [Writing Unit Tests](#writing-unit-tests).

---

## Adding a New Feature Flag

Feature flag keys follow the convention `domain:feature` (e.g., `social:feed`).

### 1. Declare the key as a constant (optional but recommended)

```csharp
// Domain/Constants/FeatureKeys.cs
public static class FeatureKeys
{
    public const string SocialFeed  = "social:feed";
    public const string SocialChat  = "social:chat";
    public const string MyNewFeature = "mymodule:myfeature";
}
```

### 2. Apply the attribute to your controller or action

```csharp
[RequireFeature(FeatureKeys.MyNewFeature)]
public class MyController : ControllerBase { ... }
```

### 3. Enable the feature for a tenant

Via the admin API:

```bash
curl -X PUT http://localhost:5000/api/admin/tenants/tenant001/features/mymodule:myfeature \
  -H "Authorization: Bearer <admin_token>" \
  -H "Content-Type: application/json" \
  -d '{ "isEnabled": true }'
```

Or add it to the development seeder in `Api/Seed/MasterDbSeeder.cs`:

```csharp
new TenantFeature { TenantId = "tenant001", FeatureKey = "mymodule:myfeature", IsEnabled = true }
```

---

## Adding a New Background Job

### Fire-and-forget job

Inject `IBackgroundJobClient` and enqueue:

```csharp
_backgroundJobClient.Enqueue<IMyService>(svc => svc.DoWorkAsync(tenantId, CancellationToken.None));
```

### Recurring job

Add the recurring job registration in `Api/Program.cs` inside the `!app.Environment.IsDevelopment()` block:

```csharp
RecurringJob.AddOrUpdate<IMyService>(
    "my-recurring-job",
    svc => svc.TriggerManualRunAsync(CancellationToken.None),
    "0 6 * * *");  // Daily at 06:00 UTC
```

Expose `TriggerManualRunAsync` as a public method on the service for Hangfire to call:

```csharp
public class MyService : BackgroundService, IMyService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await DoWorkInternalAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public Task TriggerManualRunAsync(CancellationToken ct)
        => DoWorkInternalAsync(ct);

    private async Task DoWorkInternalAsync(CancellationToken ct) { ... }
}
```

Register in `InfrastructureServiceExtensions.cs`:

```csharp
services.AddHostedService<MyService>();
```

---

## Adding a New SignalR Event

### 1. Add the method to the hub interface

```csharp
// Infrastructure/Hubs/IChatHub.cs
public interface IChatHub
{
    Task NewMessage(ChatMessageDto message);
    Task MyNewEvent(MyEventDto data);   // Add here
}
```

### 2. Call the event from a service

```csharp
public class MyService
{
    private readonly IHubContext<ChatHub, IChatHub> _chatHub;

    public async Task DoSomethingAsync(string tenantId, int userId)
    {
        // ... business logic ...
        await _chatHub.Clients
            .Group($"{tenantId}:user:{userId}")
            .MyNewEvent(new MyEventDto(...));
    }
}
```

### 3. Handle the event on the client (JavaScript)

```javascript
connection.on("MyNewEvent", (data) => {
    console.log("Received:", data);
});
```

### 4. Handle the event in Blazor (C#)

```csharp
_hubConnection.On<MyEventDto>("MyNewEvent", (data) =>
{
    // Update component state
    InvokeAsync(StateHasChanged);
});
```

---

## Writing Unit Tests

Tests live in the `Tests` project and use **xUnit + Moq + FluentAssertions**.

### Pattern for service tests with EF InMemory

```csharp
public class MyServiceTests
{
    private static BaseAppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BaseAppDbContext(options);
    }

    [Fact]
    public async Task GetSomethingAsync_ReturnsEntity_WhenExists()
    {
        // Arrange
        await using var db = CreateDb();
        db.MyEntities.Add(new MyEntity { Name = "Test" });
        await db.SaveChangesAsync();

        var service = new MyService(db);

        // Act
        var result = await service.GetSomethingAsync(1);

        // Assert
        result.Name.Should().Be("Test");
    }
}
```

### Pattern for mocking SignalR hub context

```csharp
var mockClients = new Mock<IHubClients<IChatHub>>();
var mockGroup  = new Mock<IChatHub>();
mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);

var mockHub = new Mock<IHubContext<ChatHub, IChatHub>>();
mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

var service = new ChatService(db, mockHub.Object);
```

### Known limitation: ExecuteUpdateAsync

EF Core InMemory does not support `ExecuteUpdateAsync` (used for bulk updates). Tests for methods that call `ExecuteUpdateAsync` must run against a real PostgreSQL instance (integration tests). Mark these with `[Trait("Category", "Integration")]` and skip them in unit test runs:

```bash
dotnet test --filter "Category!=Integration"
```

---

## Configuration & Secrets

### Environment Variables (production/Docker)

| Variable | Maps to config key |
|---|---|
| `ConnectionStrings__MasterDb` | `ConnectionStrings:MasterDb` |
| `MultiTenant__EncryptionKey` | `MultiTenant:EncryptionKey` |
| `Jwt__Key` | `Jwt:Key` |
| `Jwt__Issuer` | `Jwt:Issuer` |
| `Jwt__Audience` | `Jwt:Audience` |
| `App__BaseUrl` | `App:BaseUrl` |
| `Cors__AllowedOrigins__0` | First item of `Cors:AllowedOrigins` |

Double underscores (`__`) are the ASP.NET Core convention for nested config sections in environment variables.

### User Secrets (local development)

```bash
cd Api
dotnet user-secrets list      # show all secrets
dotnet user-secrets set "Key" "Value"
dotnet user-secrets remove "Key"
dotnet user-secrets clear
```

### Startup validation

The application will refuse to start if any required key is missing or still contains a `REPLACE_` placeholder. This is enforced by `RequireConfig()` in `Program.cs`. Check the startup logs if the application fails to start.

---

## Docker & Deployment

### Build images

```bash
# Build API image
docker build -t mysolutionhub-api:latest -f Api/Dockerfile .

# Build Web image
docker build -t mysolutionhub-web:latest -f Web/Dockerfile .
```

### Run with Docker Compose

```bash
# Development (no pgAdmin)
docker compose up

# Development with pgAdmin
docker compose --profile dev-tools up

# Rebuild after code changes
docker compose up --build

# Stop and remove containers (data persists in volume)
docker compose down

# Stop and remove containers AND data
docker compose down -v
```

### Environment variable overrides

Override any `docker-compose.yml` default at runtime:

```bash
JWT_KEY="production-secret" \
ENCRYPTION_KEY="production-key" \
docker compose up
```

### Production checklist

- [ ] Set all `REPLACE_` values to real secrets (never commit secrets).
- [ ] Use a strong random `Jwt:Key` (min 32 chars, ideally 64+).
- [ ] Use a 32-byte random `MultiTenant:EncryptionKey`.
- [ ] Set `Cors:AllowedOrigins` to the production frontend URL only.
- [ ] Configure `App:BaseUrl` to the public API URL for email links.
- [ ] Enable HTTPS (reverse proxy: nginx/caddy in front of the containers).
- [ ] Set up PostgreSQL with a dedicated user and limited permissions.
- [ ] Configure regular PostgreSQL backups.
- [ ] Monitor `/health/ready` with your uptime tool.
- [ ] Verify the Hangfire dashboard at `/hangfire` is only accessible to Admins.

---

## Troubleshooting

### Application won't start

> `InvalidOperationException: Configurazione obbligatoria mancante o non sostituita: 'Jwt:Key'`

A required configuration key is missing or still has a `REPLACE_` placeholder. Check:
1. `Api/appsettings.Development.json` for local development.
2. Environment variables / Docker Compose `environment:` section for containerised runs.
3. User Secrets: `dotnet user-secrets list`.

---

### `X-Tenant-Id` header missing

> `401 Unauthorized` or tenant not resolved

All requests to tenant-scoped endpoints must include the `X-Tenant-Id` header:
```
X-Tenant-Id: tenant001
```
In Swagger UI, set the `TenantId` security scheme value.

---

### EF Core migration fails

```
dotnet ef migrations add MyMigration
```

Errors to watch for:

- **"Unable to create an object of type 'BaseAppDbContext'"** — ensure the `TENANT_DB_CS` environment variable is set and the database is reachable.
- **"Sqlite:Autoincrement annotation"** — replace with `NpgsqlValueGenerationStrategy.IdentityByDefaultColumn` and add the Npgsql using directive.
- **"pending model changes"** — a property was added to an entity but no migration was created. Run `dotnet ef migrations add` before running the application.

---

### SignalR connection fails

1. Confirm the JWT is valid and not expired.
2. Check CORS: the origin must be listed in `Cors:AllowedOrigins`.
3. Check that the hub URL matches: `/hubs/chat` or `/hubs/notifications`.
4. In development, WebSocket connections may be blocked by some network proxies — SignalR will fall back to SSE or Long Polling automatically.

---

### Hangfire jobs not running

1. Verify the Hangfire dashboard at `/hangfire` (production) — check job states.
2. In development, Hangfire is registered but recurring jobs are **not** scheduled (they use the production-only block in `Program.cs`). Trigger manually via `TriggerManualRunAsync` or enqueue via code.
3. Ensure the master database is reachable — Hangfire uses it as its job store.

---

### pgAdmin: "could not connect to server"

In pgAdmin, the PostgreSQL hostname must be `postgres` (the Docker service name), not `localhost`:

```
Host: postgres
Port: 5432
Username: postgres
Password: postgres_dev
```

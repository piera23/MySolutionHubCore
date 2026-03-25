using Api.Observability;
using Api.Seed;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Domain.Interfaces;
using Hangfire;
using Infrastructure;
using MasterDb;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Startup config validation ─────────────────────────────────────────────────
static void RequireConfig(IConfiguration cfg, string key)
{
    var value = cfg[key];
    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("REPLACE_"))
        throw new InvalidOperationException(
            $"Configurazione obbligatoria mancante o non sostituita: '{key}'");
}

RequireConfig(builder.Configuration, "ConnectionStrings:MasterDb");
RequireConfig(builder.Configuration, "MultiTenant:EncryptionKey");
RequireConfig(builder.Configuration, "Jwt:Key");
RequireConfig(builder.Configuration, "Jwt:Issuer");
RequireConfig(builder.Configuration, "Jwt:Audience");

// ── Observability (OpenTelemetry) ─────────────────────────────────────────────
builder.AddObservability();

// ── CORS ──────────────────────────────────────────────────────────────────────
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5001", "https://localhost:5001"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWeb", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── API Versioning ────────────────────────────────────────────────────────────
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"),
        new QueryStringApiVersionReader("api-version"));
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Swagger con supporto multi-versione ───────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MySolutionHub API", Version = "v1" });

    c.AddSecurityDefinition("TenantId", new OpenApiSecurityScheme
    {
        Description = "Inserisci il TenantId (es. cliente1)",
        Name        = "X-Tenant-Id",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.ApiKey,
        Scheme      = "ApiKey"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization. Inserisci: Bearer {token}",
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.ApiKey,
        Scheme      = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "TenantId" }
            },
            Array.Empty<string>()
        }
    });
});

// ── SignalR + Redis backplane ─────────────────────────────────────────────────
var signalRBuilder = builder.Services.AddSignalR();
var redisCs = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisCs))
{
    signalRBuilder.AddStackExchangeRedis(redisCs, options =>
    {
        options.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal("mysolutionhub");
    });
}

// ── Master DB + Infrastructure ────────────────────────────────────────────────
builder.Services.AddMasterDb(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// ── Hangfire ──────────────────────────────────────────────────────────────────
{
    var hangfireCs = builder.Configuration.GetConnectionString("MasterDb")
        ?? throw new InvalidOperationException("Connection string 'MasterDb' non trovata per Hangfire.");

    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireCs)));

    builder.Services.AddHangfireServer(opts =>
    {
        opts.WorkerCount = 2;
        opts.Queues = ["migrations", "default"];
    });
}

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Auth endpoints: max 10 tentativi/minuto per IP (anti-brute-force)
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window               = TimeSpan.FromMinutes(1);
        opt.PermitLimit          = 10;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit           = 0;
    });

    // API generale: max 200 richieste/minuto per coppia TenantId:UserId
    // Isola il consumo tra tenant e tra utenti dello stesso tenant.
    // Fallback su IP per richieste anonime.
    options.AddPolicy<string>("api", httpContext =>
    {
        var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                       ?? "_";
        var userId   = httpContext.User?.FindFirst(
                           System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? httpContext.Connection.RemoteIpAddress?.ToString()
                       ?? "_";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{tenantId}:{userId}",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit          = 200,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 5
            });
    });

    options.RejectionStatusCode = 429;
});

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<Api.HealthChecks.MasterDbHealthCheck>("master-db", tags: ["ready"])
    .AddCheck<Api.HealthChecks.TenantDbHealthCheck>("tenant-db", tags: ["ready"]);

// ── App pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseMiddleware<Api.Middleware.GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                $"MySolutionHub API {description.ApiVersion}");
        }
    });
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var masterDb    = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
    var logFactory  = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    await masterDb.Database.MigrateAsync();

    var encryption = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();
    await MasterDbSeeder.SeedAsync(masterDb, encryption, logFactory.CreateLogger("MasterDbSeeder"));
    await TenantDbSeeder.SeedAsync(
        "Host=postgres;Port=5432;Database=mysolutionhub_tenant001;Username=postgres;Password=postgres_dev",
        logFactory.CreateLogger("TenantDbSeeder"));
}

// Hangfire dashboard (solo produzione)
if (!app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new Api.Hangfire.HangfireAdminAuthFilter()]
    });

    RecurringJob.AddOrUpdate<Infrastructure.Services.TenantMigrationHostedService>(
        "tenant-migrations",
        svc => svc.TriggerManualRunAsync(CancellationToken.None),
        "0 */12 * * *");

    RecurringJob.AddOrUpdate<Infrastructure.Services.RefreshTokenCleanupService>(
        "refresh-token-cleanup",
        svc => svc.TriggerManualRunAsync(CancellationToken.None),
        "0 3 * * *");
}

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseMultiTenant();
app.UseCors("BlazorWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHub<Infrastructure.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<Infrastructure.Hubs.ChatHub>("/hubs/chat");

app.Run();

using Api.Seed;
using Domain.Interfaces;
using Hangfire;
using Infrastructure;
using MasterDb;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

// ── Servizi ──────────────────────────────────────
// CORS: origini lette da config (dev = localhost, prod = URL del frontend)
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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MySolutionHub API", Version = "v1" });

    c.AddSecurityDefinition("TenantId", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Inserisci il TenantId (es. cliente1)",
        Name = "X-Tenant-Id",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization. Inserisci: Bearer {token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        },
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "TenantId"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddSignalR();

builder.Services.AddMasterDb(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// ── Hangfire (PostgreSQL, tutti gli ambienti) ─────────────────
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
        opts.Queues = new[] { "migrations", "default" };
    });
}

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Auth endpoints: max 10 tentativi/minuto per IP
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    // API generica: max 200 richieste/minuto per IP
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 200;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 5;
    });

    options.RejectionStatusCode = 429;
});

builder.Services.AddHealthChecks()
    .AddCheck<Api.HealthChecks.MasterDbHealthCheck>("master-db", tags: ["ready"])
    .AddCheck<Api.HealthChecks.TenantDbHealthCheck>("tenant-db", tags: ["ready"]);

// ── App ──────────────────────────────────────────
var app = builder.Build();

app.UseMiddleware<Api.Middleware.GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    await masterDb.Database.MigrateAsync();

    var encryption = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();
    await MasterDbSeeder.SeedAsync(masterDb, encryption, loggerFactory.CreateLogger("MasterDbSeeder"));
    await TenantDbSeeder.SeedAsync(
    "Host=postgres;Port=5432;Database=mysolutionhub_tenant001;Username=postgres;Password=postgres_dev",
    loggerFactory.CreateLogger("TenantDbSeeder"));
}

// ── Hangfire dashboard (solo produzione) — protetta da ruolo Admin ──
if (!app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new Api.Hangfire.HangfireAdminAuthFilter() }
    });

    // Job ricorrente: controllo migrazioni ogni 12 ore
    RecurringJob.AddOrUpdate<Infrastructure.Services.TenantMigrationHostedService>(
        "tenant-migrations",
        svc => svc.TriggerManualRunAsync(CancellationToken.None),
        "0 */12 * * *");

    // Job ricorrente: cleanup refresh token scaduti ogni 24 ore (ore 3:00 UTC)
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

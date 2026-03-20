using Api.Seed;
using Domain.Interfaces;
using Infrastructure;
using MasterDb;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Servizi ──────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorWeb", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5001",
                "https://localhost:5001",
                "http://localhost:7001",
                "https://localhost:7001")
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

    // Header X-Tenant-Id per sviluppo
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

builder.Services.AddMasterDb(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<Api.HealthChecks.MasterDbHealthCheck>("master-db", tags: ["ready"]);

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

    // Applica migration MasterDb prima del seed
    await masterDb.Database.MigrateAsync();

    var encryption = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();
    await MasterDbSeeder.SeedAsync(masterDb, encryption, loggerFactory.CreateLogger("MasterDbSeeder"));

    // Seed tenant DB con la connection string diretta
    await TenantDbSeeder.SeedAsync("Data Source=tenant001.sqlite", loggerFactory.CreateLogger("TenantDbSeeder"));
}

app.UseHttpsRedirection();

// Tenant resolution PRIMA di auth e controller
app.UseMultiTenant();       // 1. Risolvi tenant
app.UseCors("BlazorWeb");
app.UseAuthentication();    // 2. Chi sei?
app.UseAuthorization();     // 3. Cosa puoi fare?

app.MapControllers();

// Health Checks
app.MapHealthChecks("/health");                       // liveness
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// SignalR Hubs
app.MapHub<Infrastructure.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<Infrastructure.Hubs.ChatHub>("/hubs/chat");

app.Run();
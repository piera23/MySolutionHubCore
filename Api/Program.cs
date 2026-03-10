using Api.Seed;
using Infrastructure;
using MasterDb;
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

// ── App ──────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var masterDb = scope.ServiceProvider
        .GetRequiredService<MasterDb.Persistence.MasterDbContext>();
    var encryption = scope.ServiceProvider
        .GetRequiredService<Domain.Interfaces.ITenantEncryption>();

    // 1. Seed Master DB
    await MasterDbSeeder.SeedAsync(masterDb, encryption);

    // 2. Crea e migra i DB di ogni tenant attivo
    var tenants = await masterDb.TenantConnections.ToListAsync();
    foreach (var conn in tenants)
    {
        var connString = encryption.Decrypt(conn.ConnectionStringEncrypted);
        await TenantDbSeeder.SeedAsync(connString);
    }
}

app.UseHttpsRedirection();

// Tenant resolution PRIMA di auth e controller
app.UseMultiTenant();       // 1. Risolvi tenant
app.UseCors("BlazorWeb");
app.UseAuthentication();    // 2. Chi sei?
app.UseAuthorization();     // 3. Cosa puoi fare?

app.MapControllers();

// SignalR Hubs
app.MapHub<Infrastructure.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<Infrastructure.Hubs.ChatHub>("/hubs/chat");

app.Run();
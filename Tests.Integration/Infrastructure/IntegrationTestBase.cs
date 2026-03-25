using Infrastructure.Identity;
using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Base class for integration tests. Spins up a real PostgreSQL container via
/// Testcontainers and applies EF Core migrations before each test class runs.
/// Implements IAsyncLifetime so xUnit calls InitializeAsync / DisposeAsync.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    // ── Containers ────────────────────────────────────────────────────────────
    private readonly PostgreSqlContainer _masterContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("integration_master")
        .WithUsername("postgres")
        .WithPassword("postgres_test")
        .Build();

    private readonly PostgreSqlContainer _tenantContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("integration_tenant")
        .WithUsername("postgres")
        .WithPassword("postgres_test")
        .Build();

    // ── Connection strings (available after InitializeAsync) ──────────────────
    protected string MasterConnectionString  => _masterContainer.GetConnectionString();
    protected string TenantConnectionString  => _tenantContainer.GetConnectionString();

    // ── Contexts ──────────────────────────────────────────────────────────────
    protected MasterDbContext   MasterDb  { get; private set; } = null!;
    protected BaseAppDbContext  TenantDb  { get; private set; } = null!;

    // ── IConfiguration ────────────────────────────────────────────────────────
    protected IConfiguration Configuration { get; private set; } = null!;

    // ── Service provider (for Identity, etc.) ─────────────────────────────────
    protected IServiceProvider Services { get; private set; } = null!;

    // ── Tenant context ────────────────────────────────────────────────────────
    protected TenantContext TenantCtx { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start containers in parallel
        await Task.WhenAll(
            _masterContainer.StartAsync(),
            _tenantContainer.StartAsync());

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MasterDb"]  = MasterConnectionString,
                ["MultiTenant:EncryptionKey"]   = "test-encryption-key-32bytes!!!!",
                ["Jwt:Key"]                     = "integration-test-jwt-key-minimo-32-caratteri!!",
                ["Jwt:Issuer"]                  = "TestIssuer",
                ["Jwt:Audience"]                = "TestAudience"
            })
            .Build();

        // ── Master DB ─────────────────────────────────────────────────────────
        var masterOptions = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(MasterConnectionString)
            .Options;
        MasterDb = new MasterDbContext(masterOptions);
        await MasterDb.Database.MigrateAsync();
        await SeedMasterDbAsync();

        // ── Tenant context ────────────────────────────────────────────────────
        TenantCtx = new TenantContext();
        TenantCtx.SetTenant(
            "integration-tenant",
            "Integration Test Tenant",
            TenantConnectionString,
            features: []);

        // ── Tenant DB ─────────────────────────────────────────────────────────
        var tenantOptions = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseNpgsql(TenantConnectionString)
            .Options;
        TenantDb = new BaseAppDbContext(tenantOptions);
        await TenantDb.Database.MigrateAsync();

        // ── DI container ──────────────────────────────────────────────────────
        var services = new ServiceCollection();
        services.AddLogging(l => l.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton(TenantCtx);
        services.AddSingleton<ITenantContext>(TenantCtx);
        services.AddSingleton(TenantDb);
        services.AddSingleton(MasterDb);
        services.AddMemoryCache();

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireDigit        = true;
            options.Password.RequiredLength      = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase    = true;
            options.User.RequireUniqueEmail      = true;
        })
        .AddRoles<IdentityRole<int>>()
        .AddEntityFrameworkStores<BaseAppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<BaseAppDbContext>(_ => TenantDb);

        ConfigureServices(services);

        Services = services.BuildServiceProvider();

        await SeedTenantDbAsync();
    }

    public async Task DisposeAsync()
    {
        await TenantDb.DisposeAsync();
        await MasterDb.DisposeAsync();
        await _tenantContainer.StopAsync();
        await _masterContainer.StopAsync();
    }

    // ── Extension points ──────────────────────────────────────────────────────

    /// <summary>Override to register additional services for a specific test class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Override to seed the master database with test-specific data.</summary>
    protected virtual Task SeedMasterDbAsync() => Task.CompletedTask;

    /// <summary>Override to seed the tenant database with test-specific data.</summary>
    protected virtual Task SeedTenantDbAsync() => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    protected T GetService<T>() where T : notnull
        => Services.GetRequiredService<T>();

    protected IServiceScope CreateScope()
        => Services.CreateScope();

    /// <summary>
    /// Creates a fresh BaseAppDbContext connected to the tenant container.
    /// Useful when you need an isolated context that doesn't share change tracker state.
    /// </summary>
    protected BaseAppDbContext CreateFreshTenantDb()
    {
        var options = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseNpgsql(TenantConnectionString)
            .Options;
        return new BaseAppDbContext(options);
    }

    /// <summary>
    /// Creates a Tenant record in the master DB pointing at the integration tenant container.
    /// </summary>
    protected async Task<Tenant> CreateTestTenantAsync(string tenantId = "integration-tenant")
    {
        var tenant = new Tenant
        {
            TenantId  = tenantId,
            Subdomain = tenantId,
            Name      = "Integration Test Tenant",
            IsActive  = true
        };
        MasterDb.Tenants.Add(tenant);
        await MasterDb.SaveChangesAsync();
        return tenant;
    }
}

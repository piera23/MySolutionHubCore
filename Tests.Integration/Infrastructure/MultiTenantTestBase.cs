using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Estende <see cref="IntegrationTestBase"/> aggiungendo un secondo database tenant
/// (Tenant B) per i test di isolamento cross-tenant.
/// TenantDb = Tenant A  /  TenantBDb = Tenant B
/// </summary>
public abstract class MultiTenantTestBase : IntegrationTestBase
{
    private readonly PostgreSqlContainer _tenantBContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("integration_tenant_b")
        .WithUsername("postgres")
        .WithPassword("postgres_test")
        .Build();

    protected string TenantBConnectionString => _tenantBContainer.GetConnectionString();
    protected BaseAppDbContext TenantBDb { get; private set; } = null!;
    protected TenantContext TenantBCtx { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        // Avvia Tenant B container in parallelo con Master + Tenant A (avviati nella base)
        await _tenantBContainer.StartAsync();

        // Inizializza Master DB + Tenant A DB + DI container (dalla classe base)
        await base.InitializeAsync();

        // Setup Tenant B DB
        var opts = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseNpgsql(TenantBConnectionString)
            .Options;
        TenantBDb = new BaseAppDbContext(opts);
        await TenantBDb.Database.MigrateAsync();

        TenantBCtx = new TenantContext();
        TenantBCtx.SetTenant("tenant-b", "Tenant B", TenantBConnectionString, features: []);

        await SeedTenantBAsync();
    }

    public override async Task DisposeAsync()
    {
        await TenantBDb.DisposeAsync();
        await _tenantBContainer.StopAsync();

        // Pulisce Master DB + Tenant A DB (dalla classe base)
        await base.DisposeAsync();
    }

    /// <summary>Override per popolare il database di Tenant B con dati iniziali.</summary>
    protected virtual Task SeedTenantBAsync() => Task.CompletedTask;

    /// <summary>
    /// Crea un BaseAppDbContext isolato collegato al DB di Tenant B.
    /// Utile per verificare l'assenza di dati cross-tenant.
    /// </summary>
    protected BaseAppDbContext CreateFreshTenantBDb()
    {
        var opts = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseNpgsql(TenantBConnectionString)
            .Options;
        return new BaseAppDbContext(opts);
    }
}

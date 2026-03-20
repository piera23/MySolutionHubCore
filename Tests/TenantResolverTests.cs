using FluentAssertions;
using Infrastructure.MultiTenant;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public class TenantResolverTests
{
    private static MasterDbContext CreateMasterDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<MasterDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MasterDbContext(opts);
    }

    private static TenantEncryption CreateEncryption()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MultiTenant:EncryptionKey"] = "chiave-test-unitari-32-caratteri!!"
            })
            .Build();
        return new TenantEncryption(config);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsTenantContext_WhenSubdomainExists()
    {
        await using var db = CreateMasterDb(nameof(ResolveAsync_ReturnsTenantContext_WhenSubdomainExists));
        var enc = CreateEncryption();

        db.Tenants.Add(new Tenant { TenantId = "t1", Subdomain = "cliente1", Name = "Demo", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TenantConnections.Add(new TenantConnection { TenantId = "t1", ConnectionStringEncrypted = enc.Encrypt("Host=postgres;Port=5432;Database=tenant_t1;Username=postgres;Password=postgres_dev"), DbVersion = "1.0", Region = "eu-west", UpdatedAt = DateTime.UtcNow });
        db.TenantFeatures.Add(new TenantFeature { TenantId = "t1", FeatureKey = "social:feed", IsEnabled = true });
        await db.SaveChangesAsync();

        var resolver = new TenantResolver(db, new MemoryCache(new MemoryCacheOptions()), enc, NullLogger<TenantResolver>.Instance);

        var ctx = await resolver.ResolveAsync("cliente1");

        ctx.Should().NotBeNull();
        ctx!.TenantId.Should().Be("t1");
        ctx.TenantName.Should().Be("Demo");
        ctx.ConnectionString.Should().Be("Host=postgres;Port=5432;Database=tenant_t1;Username=postgres;Password=postgres_dev");
        ctx.IsFeatureEnabled("social:feed").Should().BeTrue();
        ctx.IsFeatureEnabled("social:chat").Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenSubdomainNotFound()
    {
        await using var db = CreateMasterDb(nameof(ResolveAsync_ReturnsNull_WhenSubdomainNotFound));
        var enc = CreateEncryption();
        var resolver = new TenantResolver(db, new MemoryCache(new MemoryCacheOptions()), enc, NullLogger<TenantResolver>.Instance);

        var ctx = await resolver.ResolveAsync("inesistente");

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenTenantIsInactive()
    {
        await using var db = CreateMasterDb(nameof(ResolveAsync_ReturnsNull_WhenTenantIsInactive));
        var enc = CreateEncryption();

        db.Tenants.Add(new Tenant { TenantId = "t2", Subdomain = "disattivato", Name = "Vecchia Azienda", IsActive = false, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new TenantResolver(db, new MemoryCache(new MemoryCacheOptions()), enc, NullLogger<TenantResolver>.Instance);

        var ctx = await resolver.ResolveAsync("disattivato");

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UsesCacheOnSecondCall()
    {
        await using var db = CreateMasterDb(nameof(ResolveAsync_UsesCacheOnSecondCall));
        var enc = CreateEncryption();

        db.Tenants.Add(new Tenant { TenantId = "t3", Subdomain = "cached", Name = "Cached Co", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TenantConnections.Add(new TenantConnection { TenantId = "t3", ConnectionStringEncrypted = enc.Encrypt("Host=postgres;Port=5432;Database=tenant_t3;Username=postgres;Password=postgres_dev"), DbVersion = "1.0", Region = "eu-west", UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TenantResolver(db, cache, enc, NullLogger<TenantResolver>.Instance);

        var ctx1 = await resolver.ResolveAsync("cached");
        // Rimuovi il record dal DB — la seconda chiamata deve usare la cache
        db.Tenants.RemoveRange(db.Tenants);
        await db.SaveChangesAsync();

        var ctx2 = await resolver.ResolveAsync("cached");

        ctx2.Should().NotBeNull();
        ctx2!.TenantId.Should().Be("t3");
    }
}

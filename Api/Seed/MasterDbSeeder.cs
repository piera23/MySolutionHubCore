using Domain.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;

namespace Api.Seed
{
    public static class MasterDbSeeder
    {
        public static async Task SeedAsync(
            MasterDbContext masterDb,
            ITenantEncryption encryption)
        {
            // Se ci sono già tenant non fare nulla
            if (masterDb.Tenants.Any()) return;

            // ── Tenant 1 ──────────────────────────────
            var tenant1 = new Tenant
            {
                TenantId = "tenant001",
                Subdomain = "cliente1",
                Name = "Azienda Demo Srl",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            masterDb.Tenants.Add(tenant1);
            await masterDb.SaveChangesAsync();

            // Connection string del DB tenant (SQLite per ora)
            var connString = "Data Source=tenant001.sqlite";

            masterDb.TenantConnections.Add(new TenantConnection
            {
                TenantId = tenant1.Id,
                ConnectionStringEncrypted = encryption.Encrypt(connString),
                DbVersion = "1.0.0",
                UpdatedAt = DateTime.UtcNow
            });

            // Feature abilitate per questo tenant
            masterDb.TenantFeatures.AddRange(
                new TenantFeature
                {
                    TenantId = tenant1.Id,
                    FeatureKey = "social:feed",
                    IsEnabled = true
                },
                new TenantFeature
                {
                    TenantId = tenant1.Id,
                    FeatureKey = "social:chat",
                    IsEnabled = true
                },
                new TenantFeature
                {
                    TenantId = tenant1.Id,
                    FeatureKey = "social:notifications",
                    IsEnabled = true
                }
            );

            await masterDb.SaveChangesAsync();

            Console.WriteLine("✅ Tenant001 inserito nel Master DB.");
        }
    }
}

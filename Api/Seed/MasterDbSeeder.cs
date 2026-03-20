using Domain.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Seed
{
    public static class MasterDbSeeder
    {
        public static async Task SeedAsync(
    MasterDbContext masterDb,
    ITenantEncryption encryption,
    ILogger logger)
        {
            if (await masterDb.Tenants.AnyAsync()) return;

            using var transaction = await masterDb.Database.BeginTransactionAsync();
            try
            {
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

                masterDb.TenantConnections.Add(new TenantConnection
                {
                    TenantId = tenant1.TenantId,
                    ConnectionStringEncrypted = encryption.Encrypt("Data Source=tenant001.sqlite"),
                    DbVersion = "1.0.0",
                    Region = "eu-west",
                    UpdatedAt = DateTime.UtcNow
                });

                masterDb.TenantFeatures.AddRange(
                    new TenantFeature { TenantId = tenant1.TenantId, FeatureKey = "social:feed", IsEnabled = true },
                    new TenantFeature { TenantId = tenant1.TenantId, FeatureKey = "social:chat", IsEnabled = true },
                    new TenantFeature { TenantId = tenant1.TenantId, FeatureKey = "social:notifications", IsEnabled = true }
                );

                await masterDb.SaveChangesAsync();
                await transaction.CommitAsync();
                logger.LogInformation("Tenant001 inserito nel Master DB.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "Errore durante il seed del Master DB.");
                throw;
            }
        }
    }
}

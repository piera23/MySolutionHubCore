using Application.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class TenantProvisioningService : ITenantProvisioningService
    {
        private readonly MasterDbContext _masterDb;
        private readonly ILogger<TenantProvisioningService> _logger;

        public TenantProvisioningService(
            MasterDbContext masterDb,
            ILogger<TenantProvisioningService> logger)
        {
            _masterDb = masterDb;
            _logger = logger;
        }

        public async Task<ProvisioningResult> ProvisionAsync(
            string tenantId,
            string connectionString,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Avvio provisioning per tenant {TenantId}.", tenantId);

            // Registra il tentativo nel migration log
            var log = new TenantMigrationLog
            {
                TenantId = tenantId,
                MigrationId = "InitialProvisioning",
                AppliedAt = DateTime.UtcNow,
                Status = MigrationStatus.Pending
            };
            _masterDb.TenantMigrationLogs.Add(log);
            await _masterDb.SaveChangesAsync(ct);

            try
            {
                var options = BuildOptions(connectionString);
                await using var db = new Persistence.BaseAppDbContext(options);

                // Crea il DB e applica tutte le migrazioni pendenti
                await db.Database.MigrateAsync(ct);
                _logger.LogInformation("Migrazioni applicate per tenant {TenantId}.", tenantId);

                // Seed ruoli base
                await SeedRolesAsync(db, ct);
                _logger.LogInformation("Ruoli base creati per tenant {TenantId}.", tenantId);

                // Aggiorna il log con esito positivo
                log.Status = MigrationStatus.Completed;
                await _masterDb.SaveChangesAsync(ct);

                _logger.LogInformation("Provisioning completato per tenant {TenantId}.", tenantId);
                return new ProvisioningResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provisioning fallito per tenant {TenantId}.", tenantId);
                log.Status = MigrationStatus.Failed;
                log.ErrorMessage = ex.Message;
                await _masterDb.SaveChangesAsync(ct);

                return new ProvisioningResult(false, ex.Message);
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        private static DbContextOptions BuildOptions(string connectionString)
        {
            var builder = new DbContextOptionsBuilder();
            builder.UseNpgsql(connectionString);
            return builder.Options;
        }

        private static async Task SeedRolesAsync(
            Persistence.BaseAppDbContext db,
            CancellationToken ct)
        {
            var roleStore = new RoleStore<IdentityRole<int>, Persistence.BaseAppDbContext, int>(db);
            var roleManager = new RoleManager<IdentityRole<int>>(
                roleStore,
                Array.Empty<IRoleValidator<IdentityRole<int>>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!);

            foreach (var role in new[] { "Internal", "External", "Admin" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
            }
        }
    }
}

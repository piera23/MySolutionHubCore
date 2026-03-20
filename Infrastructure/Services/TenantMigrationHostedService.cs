using Domain.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    /// <summary>
    /// Background service che verifica periodicamente le migrazioni pendenti
    /// su tutti i tenant attivi e le applica automaticamente.
    /// Viene eseguito: una volta all'avvio (dopo 30s) e poi ogni 12 ore.
    /// </summary>
    public class TenantMigrationHostedService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TenantMigrationHostedService> _logger;

        public TenantMigrationHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<TenantMigrationHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Punto di ingresso per trigger manuali da Hangfire o da endpoint admin.
        /// </summary>
        public async Task TriggerManualRunAsync(CancellationToken ct)
        {
            _logger.LogInformation("Migrazione tenant avviata manualmente.");
            await RunMigrationCheckAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "TenantMigrationHostedService avviato. Prima esecuzione tra {Delay}s.",
                StartupDelay.TotalSeconds);

            // Attesa iniziale per lasciar stabilizzare l'app
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunMigrationCheckAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunMigrationCheckAsync(CancellationToken ct)
        {
            _logger.LogInformation("Avvio controllo migrazioni tenant.");

            using var scope = _scopeFactory.CreateScope();
            var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            var encryption = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();

            var tenants = await masterDb.Tenants
                .Where(t => t.IsActive)
                .Include(t => t.Connections)
                .ToListAsync(ct);

            _logger.LogInformation("Tenant attivi da controllare: {Count}.", tenants.Count);

            foreach (var tenant in tenants)
            {
                if (ct.IsCancellationRequested) break;

                var connection = tenant.Connections.FirstOrDefault();
                if (connection is null)
                {
                    _logger.LogWarning(
                        "Nessuna connection string per tenant {TenantId}, salto.", tenant.TenantId);
                    continue;
                }

                try
                {
                    var plainCs = encryption.Decrypt(connection.ConnectionStringEncrypted);
                    await ApplyPendingMigrationsAsync(masterDb, tenant.TenantId, plainCs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Errore controllo migrazioni per tenant {TenantId}.", tenant.TenantId);
                }
            }

            _logger.LogInformation("Controllo migrazioni completato.");
        }

        private async Task ApplyPendingMigrationsAsync(
            MasterDbContext masterDb,
            string tenantId,
            string connectionString,
            CancellationToken ct)
        {
            var options = new DbContextOptionsBuilder();
            options.UseNpgsql(connectionString);

            await using var db = new Persistence.BaseAppDbContext(options.Options);
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (!pending.Any())
            {
                _logger.LogDebug("Nessuna migrazione pendente per tenant {TenantId}.", tenantId);
                return;
            }

            _logger.LogInformation(
                "Applicazione di {Count} migrazione/i pendente/i per tenant {TenantId}: {Migrations}.",
                pending.Count, tenantId, string.Join(", ", pending));

            var log = new TenantMigrationLog
            {
                TenantId = tenantId,
                MigrationId = string.Join(", ", pending),
                AppliedAt = DateTime.UtcNow,
                Status = MigrationStatus.Pending
            };
            masterDb.TenantMigrationLogs.Add(log);
            await masterDb.SaveChangesAsync(ct);

            try
            {
                await db.Database.MigrateAsync(ct);

                log.Status = MigrationStatus.Completed;
                await masterDb.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Migrazioni applicate con successo per tenant {TenantId}.", tenantId);
            }
            catch (Exception ex)
            {
                log.Status = MigrationStatus.Failed;
                log.ErrorMessage = ex.Message;
                await masterDb.SaveChangesAsync(ct);

                _logger.LogError(ex,
                    "Fallimento applicazione migrazioni per tenant {TenantId}.", tenantId);
            }
        }
    }
}

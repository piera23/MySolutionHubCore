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
    /// Scheduling: avvio dopo 30s, poi ogni 12 ore.
    ///
    /// Garanzie:
    ///   - Distributed soft-lock: controlla nel MasterDB se un'altra istanza sta già
    ///     migrando lo stesso tenant (entry Pending < 30 min), evitando run concorrenti.
    ///   - Retry con backoff esponenziale: max <see cref="MaxRetryAttempts"/> tentativi
    ///     (1 s, 2 s, 4 s) in caso di errori transitori del DB.
    /// </summary>
    public class TenantMigrationHostedService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay  = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Interval      = TimeSpan.FromHours(12);
        private static readonly TimeSpan LockTimeout   = TimeSpan.FromMinutes(30);
        private const int MaxRetryAttempts = 3;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TenantMigrationHostedService> _logger;

        public TenantMigrationHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<TenantMigrationHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>Punto di ingresso per trigger manuali da Hangfire o endpoint admin.</summary>
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

            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunMigrationCheckAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }

        // ── Core logic ────────────────────────────────────────────────────────

        private async Task RunMigrationCheckAsync(CancellationToken ct)
        {
            _logger.LogInformation("Avvio controllo migrazioni tenant.");

            using var scope    = _scopeFactory.CreateScope();
            var masterDb       = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            var encryption     = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();

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
            // ── 1. Soft distributed lock ──────────────────────────────────────
            // Se esiste già un log Pending per questo tenant creato negli ultimi
            // LockTimeout, un'altra istanza sta probabilmente già eseguendo la
            // migrazione. Saltiamo per evitare run concorrenti.
            var lockThreshold = DateTime.UtcNow.Subtract(LockTimeout);
            var isLocked = await masterDb.TenantMigrationLogs
                .AnyAsync(l =>
                    l.TenantId  == tenantId &&
                    l.Status    == MigrationStatus.Pending &&
                    l.AppliedAt >= lockThreshold, ct);

            if (isLocked)
            {
                _logger.LogDebug(
                    "Migrazione tenant {TenantId} già in esecuzione su un'altra istanza, salto.",
                    tenantId);
                return;
            }

            // ── 2. Controlla migrazioni pendenti ──────────────────────────────
            var options = new DbContextOptionsBuilder();
            options.UseNpgsql(connectionString);

            await using var db  = new Persistence.BaseAppDbContext(options.Options);
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (!pending.Any())
            {
                _logger.LogDebug("Nessuna migrazione pendente per tenant {TenantId}.", tenantId);
                return;
            }

            _logger.LogInformation(
                "Applicazione di {Count} migrazione/i per tenant {TenantId}: {Names}.",
                pending.Count, tenantId, string.Join(", ", pending));

            // ── 3. Registra tentativo (funge anche da lock) ───────────────────
            var log = new TenantMigrationLog
            {
                TenantId    = tenantId,
                MigrationId = string.Join(", ", pending),
                AppliedAt   = DateTime.UtcNow,
                Status      = MigrationStatus.Pending
            };
            masterDb.TenantMigrationLogs.Add(log);
            await masterDb.SaveChangesAsync(ct);

            // ── 4. Migrazione con retry + backoff esponenziale ────────────────
            Exception? lastEx = null;
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync(ct);
                    lastEx = null;
                    break;
                }
                catch (Exception ex) when (attempt < MaxRetryAttempts)
                {
                    lastEx = ex;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
                    _logger.LogWarning(ex,
                        "Migrazione tenant {TenantId}: tentativo {Attempt}/{Max} fallito. " +
                        "Retry tra {Delay}s.",
                        tenantId, attempt, MaxRetryAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            // ── 5. Aggiorna log ───────────────────────────────────────────────
            if (lastEx is null)
            {
                log.Status = MigrationStatus.Completed;
                _logger.LogInformation(
                    "Migrazioni applicate con successo per tenant {TenantId}.", tenantId);
            }
            else
            {
                log.Status       = MigrationStatus.Failed;
                log.ErrorMessage = lastEx.Message;
                _logger.LogError(lastEx,
                    "Fallimento definitivo migrazioni per tenant {TenantId} " +
                    "dopo {Max} tentativi.", tenantId, MaxRetryAttempts);
            }

            await masterDb.SaveChangesAsync(ct);
        }
    }
}

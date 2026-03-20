using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    /// <summary>
    /// Background service che elimina periodicamente i refresh token
    /// scaduti o revocati per evitare la crescita illimitata della tabella.
    /// Eseguito: una volta all'avvio (dopo 5 min) e poi ogni 24 ore.
    /// In produzione (con Hangfire) viene schedulato anche come recurring job.
    /// </summary>
    public class RefreshTokenCleanupService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        /// <summary>
        /// Mantieni i token revocati per 7 giorni (audit trail minimo),
        /// poi elimina anche quelli; i token scaduti e non revocati
        /// vengono eliminati dopo 30 giorni.
        /// </summary>
        private static readonly TimeSpan RevokedRetention = TimeSpan.FromDays(7);
        private static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RefreshTokenCleanupService> _logger;

        public RefreshTokenCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<RefreshTokenCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>Punto di ingresso per trigger manuali da Hangfire.</summary>
        public async Task TriggerManualRunAsync(CancellationToken ct)
        {
            _logger.LogInformation("Cleanup refresh token avviato manualmente.");
            await RunCleanupAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "RefreshTokenCleanupService avviato. Prima esecuzione tra {Delay} min.",
                StartupDelay.TotalMinutes);

            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunCleanupAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCleanupAsync(CancellationToken ct)
        {
            _logger.LogInformation("Avvio cleanup refresh token scaduti/revocati.");

            using var scope = _scopeFactory.CreateScope();
            var tenantFactory = scope.ServiceProvider
                .GetRequiredService<Domain.Interfaces.ITenantDbContextFactory>();

            // Iteriamo su tutti i tenant attivi tramite MasterDb
            var masterDb = scope.ServiceProvider
                .GetRequiredService<MasterDb.Persistence.MasterDbContext>();
            var encryption = scope.ServiceProvider
                .GetRequiredService<Domain.Interfaces.ITenantEncryption>();

            var tenants = await masterDb.Tenants
                .Where(t => t.IsActive)
                .Include(t => t.Connections)
                .ToListAsync(ct);

            var totalDeleted = 0;

            foreach (var tenant in tenants)
            {
                if (ct.IsCancellationRequested) break;

                var connection = tenant.Connections.FirstOrDefault();
                if (connection is null) continue;

                try
                {
                    var plainCs = encryption.Decrypt(connection.ConnectionStringEncrypted);
                    var options = new DbContextOptionsBuilder<BaseAppDbContext>();
                    options.UseNpgsql(plainCs);

                    await using var db = new BaseAppDbContext(options.Options);

                    var revokedCutoff = DateTime.UtcNow - RevokedRetention;
                    var expiredCutoff = DateTime.UtcNow - ExpiredRetention;

                    var toDelete = await db.RefreshTokens
                        .IgnoreQueryFilters()
                        .Where(r =>
                            (r.IsRevoked && r.UpdatedAt < revokedCutoff) ||
                            (!r.IsRevoked && r.ExpiresAt < expiredCutoff))
                        .ToListAsync(ct);

                    if (toDelete.Count > 0)
                    {
                        db.RefreshTokens.RemoveRange(toDelete);
                        await db.SaveChangesAsync(ct);
                        totalDeleted += toDelete.Count;

                        _logger.LogInformation(
                            "Eliminati {Count} refresh token per tenant {TenantId}.",
                            toDelete.Count, tenant.TenantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Errore cleanup refresh token per tenant {TenantId}.", tenant.TenantId);
                }
            }

            _logger.LogInformation(
                "Cleanup completato. Token eliminati in totale: {Total}.", totalDeleted);
        }
    }
}

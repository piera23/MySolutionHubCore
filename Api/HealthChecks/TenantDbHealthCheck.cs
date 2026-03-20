using Domain.Interfaces;
using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.HealthChecks
{
    /// <summary>
    /// Verifica che almeno un tenant attivo abbia il proprio DB raggiungibile.
    /// Usato come readiness probe per segnalare problemi di connettività ai DB tenant.
    /// </summary>
    public class TenantDbHealthCheck : IHealthCheck
    {
        private readonly MasterDbContext _masterDb;
        private readonly ITenantEncryption _encryption;

        public TenantDbHealthCheck(
            MasterDbContext masterDb,
            ITenantEncryption encryption)
        {
            _masterDb = masterDb;
            _encryption = encryption;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Prende il primo tenant attivo con una connection string
                var connection = await _masterDb.TenantConnections
                    .Join(_masterDb.Tenants.Where(t => t.IsActive),
                        c => c.TenantId,
                        t => t.TenantId,
                        (c, t) => c)
                    .FirstOrDefaultAsync(cancellationToken);

                if (connection is null)
                    return HealthCheckResult.Degraded("Nessun tenant attivo trovato.");

                var plainCs = _encryption.Decrypt(connection.ConnectionStringEncrypted);

                var options = new DbContextOptionsBuilder();
                options.UseNpgsql(plainCs);

                await using var db = new Infrastructure.Persistence.BaseAppDbContext(options.Options);
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);

                return canConnect
                    ? HealthCheckResult.Healthy("Tenant DB raggiungibile.")
                    : HealthCheckResult.Unhealthy("Tenant DB non raggiungibile.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Errore connessione Tenant DB.", ex);
            }
        }
    }
}

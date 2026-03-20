using MasterDb.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.HealthChecks
{
    public class MasterDbHealthCheck : IHealthCheck
    {
        private readonly MasterDbContext _db;

        public MasterDbHealthCheck(MasterDbContext db)
        {
            _db = db;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("Master DB raggiungibile.")
                    : HealthCheckResult.Unhealthy("Master DB non raggiungibile.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Errore connessione Master DB.", ex);
            }
        }
    }
}

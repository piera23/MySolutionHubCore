using Domain.Interfaces;
using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.MultiTenant
{
    public class TenantResolver : ITenantResolver
    {
        private readonly MasterDbContext _masterDb;
        private readonly IMemoryCache _cache;
        private readonly ITenantEncryption _encryption;
        private readonly ILogger<TenantResolver> _logger;

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        public TenantResolver(
            MasterDbContext masterDb,
            IMemoryCache cache,
            ITenantEncryption encryption,
            ILogger<TenantResolver> logger)
        {
            _masterDb = masterDb;
            _cache = cache;
            _encryption = encryption;
            _logger = logger;
        }

        public async Task<ITenantContext?> ResolveAsync(
    string subdomain,
    CancellationToken ct = default)
        {
            var cacheKey = $"tenant:{subdomain}";

            if (_cache.TryGetValue(cacheKey, out TenantCacheEntry? cached) && cached is not null)
                return BuildContext(cached);

            // Cerca per subdomain
            var tenant = await _masterDb.Tenants
                .Where(t => t.Subdomain == subdomain && t.IsActive)
                .Select(t => new { t.TenantId, t.Name })
                .FirstOrDefaultAsync(ct);

            if (tenant is null)
            {
                _logger.LogWarning("Tenant non trovato per subdomain: {Subdomain}", subdomain);
                return null;
            }

            // Usa TenantId (stringa) per cercare la connessione
            var connection = await _masterDb.TenantConnections
                .Where(c => c.TenantId == tenant.TenantId)
                .FirstOrDefaultAsync(ct);

            if (connection is null)
            {
                _logger.LogError("Nessuna connection string per tenant: {TenantId}", tenant.TenantId);
                return null;
            }

            var features = await _masterDb.TenantFeatures
                .Where(f => f.TenantId == tenant.TenantId && f.IsEnabled)
                .Select(f => f.FeatureKey)
                .ToListAsync(ct);

            var entry = new TenantCacheEntry
            {
                TenantId = tenant.TenantId,
                TenantName = tenant.Name,
                ConnectionStringEncrypted = connection.ConnectionStringEncrypted,
                EnabledFeatures = features,
            };

            _cache.Set(cacheKey, entry, CacheTtl);
            return BuildContext(entry);
        }

        private ITenantContext BuildContext(TenantCacheEntry entry)
        {
            var ctx = new TenantContext();
            ctx.SetTenant(
                entry.TenantId,
                entry.TenantName,
                _encryption.Decrypt(entry.ConnectionStringEncrypted),
                entry.EnabledFeatures,
                entry.Settings);
            return ctx;
        }
    }
}

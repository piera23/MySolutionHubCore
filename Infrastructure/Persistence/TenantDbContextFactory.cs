using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence
{
    public class TenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly ITenantContext _tenantContext;
        private readonly IHostEnvironment _env;
        private readonly ILogger<TenantDbContextFactory> _logger;

        public TenantDbContextFactory(
            ITenantContext tenantContext,
            IHostEnvironment env,
            ILogger<TenantDbContextFactory> logger)
        {
            _tenantContext = tenantContext;
            _env = env;
            _logger = logger;
        }

        public DbContext Create()
        {
            if (string.IsNullOrEmpty(_tenantContext.ConnectionString))
                throw new InvalidOperationException(
                    "TenantContext non inizializzato.");

            var options = new DbContextOptionsBuilder();

            if (_env.IsDevelopment())
                options.UseSqlite(_tenantContext.ConnectionString);
            else
                options.UseSqlServer(_tenantContext.ConnectionString);

            return _tenantContext.TenantId switch
            {
                // "tenant001" => new Tenant001DbContext(options.Options),
                _ => new BaseAppDbContext(options.Options)
            };
        }
    }
}

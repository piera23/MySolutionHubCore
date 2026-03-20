using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MasterDb
{
    public static class MasterDbServiceExtensions
    {
        public static IServiceCollection AddMasterDb(
            this IServiceCollection services,
            IConfiguration config)
        {
            var connectionString = config.GetConnectionString("MasterDb")
                ?? throw new InvalidOperationException(
                    "Connection string 'MasterDb' non trovata.");

            services.AddDbContext<MasterDbContext>(options =>
                options.UseNpgsql(connectionString));

            return services;
        }
    }
}

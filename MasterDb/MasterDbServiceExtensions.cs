using MasterDb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MasterDb
{
    public static class MasterDbServiceExtensions
    {
        public static IServiceCollection AddMasterDb(
            this IServiceCollection services,
            IConfiguration config,
            IHostEnvironment env)
        {
            var connectionString = config.GetConnectionString("MasterDb")
                ?? throw new InvalidOperationException(
                    "Connection string 'MasterDb' non trovata.");

            services.AddDbContext<MasterDbContext>(options =>
            {
                if (env.IsDevelopment())
                    options.UseSqlite(connectionString);
                else
                    options.UseSqlServer(connectionString);
            });

            return services;
        }
    }
}

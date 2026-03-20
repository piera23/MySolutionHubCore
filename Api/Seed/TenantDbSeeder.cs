using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Seed
{
    public static class TenantDbSeeder
    {
        public static async Task SeedAsync(string connectionString, ILogger logger)
        {
            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            await using var db = new BaseAppDbContext(options);

            // Applica migration pendenti
            await db.Database.MigrateAsync();

            // Crea ruoli base se non esistono
            var roleStore = new RoleStore<IdentityRole<int>, BaseAppDbContext, int>(db);
            var roleManager = new RoleManager<IdentityRole<int>>(
                roleStore,
                Array.Empty<IRoleValidator<IdentityRole<int>>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!);

            foreach (var role in new[] { "Internal", "External", "Admin" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
                    logger.LogInformation("Ruolo '{Role}' creato.", role);
                }
            }

            logger.LogInformation("DB tenant migrato: {ConnectionString}", connectionString);
        }
    }
}

using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Api.Seed
{
    public static class TenantDbSeeder
    {
        public static async Task SeedAsync(string connectionString)
        {
            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseSqlite(connectionString)
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
                    Console.WriteLine($"✅ Ruolo '{role}' creato.");
                }
            }

            Console.WriteLine($"✅ DB tenant migrato: {connectionString}");
        }
    }
}

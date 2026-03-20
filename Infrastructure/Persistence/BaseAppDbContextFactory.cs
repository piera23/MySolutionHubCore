using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence
{
    public class BaseAppDbContextFactory : IDesignTimeDbContextFactory<BaseAppDbContext>
    {
        public BaseAppDbContext CreateDbContext(string[] args)
        {
            // Design-time factory: usa la variabile d'ambiente o un default locale
            var cs = Environment.GetEnvironmentVariable("TENANT_DB_CS")
                ?? "Host=localhost;Port=5432;Database=mysolutionhub_tenant_design;Username=postgres;Password=postgres_dev";

            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseNpgsql(cs)
                .Options;

            return new BaseAppDbContext(options);
        }
    }
}

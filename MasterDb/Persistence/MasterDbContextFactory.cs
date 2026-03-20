using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Persistence
{
    public class MasterDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
    {
        public MasterDbContext CreateDbContext(string[] args)
        {
            var cs = Environment.GetEnvironmentVariable("MASTERDB_CS")
                ?? "Host=localhost;Port=5432;Database=mysolutionhub_master;Username=postgres;Password=postgres_dev";

            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseNpgsql(cs)
                .Options;

            return new MasterDbContext(options);
        }
    }
}

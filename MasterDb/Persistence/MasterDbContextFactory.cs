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
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlite("Data Source=masterdb.sqlite")
                .Options;

            return new MasterDbContext(options);
        }
    }
}

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
            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseSqlite("Data Source=tenant_design.sqlite")
                .Options;

            return new BaseAppDbContext(options);
        }
    }
}

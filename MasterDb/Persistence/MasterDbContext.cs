using MasterDb.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Persistence
{
    public class MasterDbContext : DbContext
    {
        public MasterDbContext(DbContextOptions<MasterDbContext> options)
            : base(options) { }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantConnection> TenantConnections => Set<TenantConnection>();
        public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();
        public DbSet<TenantMigrationLog> TenantMigrationLogs => Set<TenantMigrationLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Tenant>(e =>
            {
                e.HasKey(t => t.Id);
                e.HasIndex(t => t.TenantId).IsUnique();
                e.HasIndex(t => t.Subdomain).IsUnique();

                e.HasMany(t => t.Connections)
                 .WithOne()
                 .HasForeignKey(c => c.TenantId)
                 .HasPrincipalKey(t => t.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(t => t.Features)
                 .WithOne()
                 .HasForeignKey(f => f.TenantId)
                 .HasPrincipalKey(t => t.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(t => t.MigrationLogs)
                 .WithOne()
                 .HasForeignKey(m => m.TenantId)
                 .HasPrincipalKey(t => t.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

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

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // Tenant
            mb.Entity<Tenant>(e =>
            {
                e.HasKey(t => t.Id);
                e.HasIndex(t => t.Subdomain).IsUnique();
                e.HasIndex(t => t.TenantId).IsUnique();
                e.Property(t => t.TenantId).HasMaxLength(50).IsRequired();
                e.Property(t => t.Subdomain).HasMaxLength(100).IsRequired();
                e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            });

            // TenantConnection
            mb.Entity<TenantConnection>(e =>
            {
                e.HasKey(tc => tc.Id);
                e.HasOne(tc => tc.Tenant)
                 .WithMany()
                 .HasForeignKey(tc => tc.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.Property(tc => tc.ConnectionStringEncrypted).IsRequired();
            });

            // TenantFeature
            mb.Entity<TenantFeature>(e =>
            {
                e.HasKey(tf => tf.Id);
                e.HasOne(tf => tf.Tenant)
                 .WithMany()
                 .HasForeignKey(tf => tf.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(tf => new { tf.TenantId, tf.FeatureKey }).IsUnique();
                e.Property(tf => tf.FeatureKey).HasMaxLength(100).IsRequired();
            });

            // TenantMigrationLog
            mb.Entity<TenantMigrationLog>(e =>
            {
                e.HasKey(ml => ml.Id);
                e.HasOne(ml => ml.Tenant)
                 .WithMany()
                 .HasForeignKey(ml => ml.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

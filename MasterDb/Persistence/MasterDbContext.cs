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
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

                e.HasMany(t => t.Settings)
                 .WithOne()
                 .HasForeignKey(s => s.TenantId)
                 .HasPrincipalKey(t => t.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantSetting>(e =>
            {
                e.HasKey(s => s.Id);
                e.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
                e.Property(s => s.Key).HasMaxLength(200).IsRequired();
                e.Property(s => s.Value).HasMaxLength(2000).IsRequired();
            });

            modelBuilder.Entity<AuditLog>(e =>
            {
                e.HasKey(a => a.Id);
                e.HasIndex(a => a.TenantId);
                e.HasIndex(a => a.Timestamp);
                e.Property(a => a.Action).HasMaxLength(100).IsRequired();
                e.Property(a => a.ActorId).HasMaxLength(100).IsRequired();
                e.Property(a => a.ActorName).HasMaxLength(256).IsRequired();
                e.Property(a => a.EntityType).HasMaxLength(100);
                e.Property(a => a.EntityId).HasMaxLength(100);
                e.Property(a => a.IpAddress).HasMaxLength(45);
                e.Property(a => a.Changes).HasMaxLength(4000);
            });
        }
    }
}

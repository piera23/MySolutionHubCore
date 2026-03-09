using Domain.Entities;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence
{
    public class BaseAppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
    {
        public BaseAppDbContext(DbContextOptions options) : base(options) { }

        // Social Layer
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
        public DbSet<UserFollow> UserFollows => Set<UserFollow>();
        public DbSet<ActivityReaction> ActivityReactions => Set<ActivityReaction>();
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<MessageReadStatus> MessageReadStatuses => Set<MessageReadStatus>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // Identity tables
            mb.Entity<ApplicationUser>(e =>
            {
                e.HasIndex(u => u.Email).IsUnique();
                e.HasIndex(u => u.UserName).IsUnique();
                e.Property(u => u.FirstName).HasMaxLength(100);
                e.Property(u => u.LastName).HasMaxLength(100);
                e.HasQueryFilter(u => !u.IsDeleted);
            });
            mb.Entity<ApplicationUser>().ToTable("Users");
            mb.Entity<IdentityRole<int>>().ToTable("Roles");
            mb.Entity<IdentityUserRole<int>>().ToTable("UserRoles");
            mb.Entity<IdentityUserClaim<int>>().ToTable("UserClaims");
            mb.Entity<IdentityUserLogin<int>>().ToTable("UserLogins");
            mb.Entity<IdentityRoleClaim<int>>().ToTable("RoleClaims");
            mb.Entity<IdentityUserToken<int>>().ToTable("UserTokens");

            // Notifiche
            mb.Entity<Notification>(e =>
            {
                e.HasKey(n => n.Id);
                e.HasIndex(n => new { n.UserId, n.IsRead });
                e.Property(n => n.Type).HasMaxLength(100).IsRequired();
                e.Property(n => n.Title).HasMaxLength(200).IsRequired();
                e.HasQueryFilter(n => !n.IsDeleted);
            });

            // Activity Events
            mb.Entity<ActivityEvent>(e =>
            {
                e.HasKey(a => a.Id);
                e.HasIndex(a => new { a.UserId, a.CreatedAt });
                e.Property(a => a.EventType).HasMaxLength(100).IsRequired();
                e.HasQueryFilter(a => !a.IsDeleted);
            });

            // UserFollow
            mb.Entity<UserFollow>(e =>
            {
                e.HasKey(f => f.Id);
                e.HasIndex(f => new { f.FollowerId, f.FollowedId }).IsUnique();
                e.HasQueryFilter(f => !f.IsDeleted);
            });

            // ActivityReaction
            mb.Entity<ActivityReaction>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => new { r.EventId, r.UserId }).IsUnique();
                e.HasQueryFilter(r => !r.IsDeleted);
            });

            // Chat
            mb.Entity<ChatConversation>(e =>
            {
                e.HasKey(c => c.Id);
                e.HasMany(c => c.Participants).WithOne(p => p.Conversation)
                 .HasForeignKey(p => p.ConversationId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(c => c.Messages).WithOne(m => m.Conversation)
                 .HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
                e.HasQueryFilter(c => !c.IsDeleted);
            });

            mb.Entity<ChatParticipant>(e =>
            {
                e.HasKey(p => p.Id);
                e.HasIndex(p => new { p.ConversationId, p.UserId }).IsUnique();
                e.HasQueryFilter(p => !p.IsDeleted);
            });

            mb.Entity<ChatMessage>(e =>
            {
                e.HasKey(m => m.Id);
                e.HasIndex(m => new { m.ConversationId, m.SentAt });
                e.Property(m => m.Body).IsRequired();
                e.HasMany(m => m.ReadStatuses).WithOne(r => r.Message)
                 .HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
                e.HasQueryFilter(m => !m.IsDeleted);
            });

            mb.Entity<MessageReadStatus>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => new { r.MessageId, r.UserId }).IsUnique();
            });

            ConfigureTenantSpecific(mb);
        }

        protected virtual void ConfigureTenantSpecific(ModelBuilder mb) { }
    }
}

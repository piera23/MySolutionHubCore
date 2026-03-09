using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class ActivityService : IActivityService
    {
        private readonly ITenantDbContextFactory _dbFactory;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<ActivityService> _logger;

        public ActivityService(
            ITenantDbContextFactory dbFactory,
            ITenantContext tenantContext,
            ILogger<ActivityService> logger)
        {
            _dbFactory = dbFactory;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async Task PublishAsync(
            int userId,
            string eventType,
            string? entityId = null,
            string? entityType = null,
            object? payload = null,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var activity = new ActivityEvent
            {
                UserId = userId,
                EventType = eventType,
                EntityId = entityId,
                EntityType = entityType,
                Payload = payload is not null
                    ? JsonSerializer.Serialize(payload)
                    : null,
                IsPublic = true,
                CreatedAt = DateTime.UtcNow
            };

            db.ActivityEvents.Add(activity);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Activity pubblicata — Tenant: {TenantId}, User: {UserId}, Type: {EventType}",
                _tenantContext.TenantId, userId, eventType);
        }

        public async Task<IEnumerable<ActivityEventDto>> GetFeedAsync(
            int userId,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            // Recupera gli utenti seguiti
            var followedIds = await db.UserFollows
                .Where(f => f.FollowerId == userId && f.IsActive)
                .Select(f => f.FollowedId)
                .ToListAsync(ct);

            // Includi anche le attività dell'utente stesso
            followedIds.Add(userId);

            // Recupera le reazioni dell'utente
            var userReactions = await db.ActivityReactions
                .Where(r => r.UserId == userId)
                .Select(r => r.EventId)
                .ToListAsync(ct);

            var events = await db.ActivityEvents
                .Where(e => followedIds.Contains(e.UserId) && e.IsPublic)
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(db.Users,
                    e => e.UserId,
                    u => u.Id,
                    (e, u) => new { Event = e, User = u })
                .ToListAsync(ct);

            var eventIds = events.Select(x => x.Event.Id).ToList();

            var reactionCounts = await db.ActivityReactions
                .Where(r => eventIds.Contains(r.EventId))
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count, ct);

            return events.Select(x => new ActivityEventDto(
                Id: x.Event.Id,
                UserId: x.Event.UserId,
                Username: x.User.UserName ?? "",
                AvatarUrl: x.User.AvatarUrl,
                EventType: x.Event.EventType,
                EntityId: x.Event.EntityId,
                EntityType: x.Event.EntityType,
                Payload: x.Event.Payload,
                ReactionsCount: reactionCounts.GetValueOrDefault(x.Event.Id, 0),
                HasReacted: userReactions.Contains(x.Event.Id),
                CreatedAt: x.Event.CreatedAt
            ));
        }

        public async Task FollowAsync(
            int followerId,
            int followedId,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var existing = await db.UserFollows
                .FirstOrDefaultAsync(f =>
                    f.FollowerId == followerId &&
                    f.FollowedId == followedId, ct);

            if (existing is not null)
            {
                existing.IsActive = true;
            }
            else
            {
                db.UserFollows.Add(new UserFollow
                {
                    FollowerId = followerId,
                    FollowedId = followedId,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task UnfollowAsync(
            int followerId,
            int followedId,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            await db.UserFollows
                .Where(f => f.FollowerId == followerId && f.FollowedId == followedId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsActive, false), ct);
        }

        public async Task ReactAsync(
            int eventId,
            int userId,
            string reactionType = "like",
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var existing = await db.ActivityReactions
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId, ct);

            if (existing is not null)
            {
                // Toggle — rimuovi la reazione se già presente
                db.ActivityReactions.Remove(existing);
            }
            else
            {
                db.ActivityReactions.Add(new ActivityReaction
                {
                    EventId = eventId,
                    UserId = userId,
                    ReactionType = reactionType
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }
}

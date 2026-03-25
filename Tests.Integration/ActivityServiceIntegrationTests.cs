using Application.Interfaces;
using FluentAssertions;
using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Integration tests for ActivityService using a real PostgreSQL database.
/// Covers follow/unfollow and feed queries that use ExecuteUpdateAsync.
/// </summary>
[Trait("Category", "Integration")]
public class ActivityServiceIntegrationTests : IntegrationTestBase
{
    private IActivityService CreateService()
    {
        var factory = new DirectTenantDbContextFactory(TenantConnectionString);
        var logger  = GetService<ILoggerFactory>().CreateLogger<ActivityService>();
        return new ActivityService(factory, TenantCtx, logger);
    }

    [Fact]
    public async Task PublishAsync_PersistsActivityEvent()
    {
        var svc = CreateService();

        await svc.PublishAsync(
            userId:     1,
            eventType:  "post:create",
            entityId:   "42",
            entityType: "Post",
            payload:    new { title = "Hello world" });

        await using var db = CreateFreshTenantDb();
        var events = await db.ActivityEvents
            .Where(e => e.UserId == 1 && e.EventType == "post:create")
            .ToListAsync();

        events.Should().HaveCount(1);
        events[0].EntityId.Should().Be("42");
    }

    [Fact]
    public async Task GetFeedAsync_ReturnsPublishedEvents_ForFollowedUsers()
    {
        var svc = CreateService();

        // User 1 follows user 2
        await svc.FollowAsync(followerId: 1, followedId: 2);

        // User 2 publishes an event
        await svc.PublishAsync(userId: 2, eventType: "post:create", entityId: "10");

        var feed = (await svc.GetFeedAsync(userId: 1, page: 1, pageSize: 20)).ToList();

        feed.Should().NotBeEmpty();
        feed.Any(e => e.UserId == 2 && e.EventType == "post:create").Should().BeTrue();
    }

    [Fact]
    public async Task GetFeedAsync_DoesNotReturn_EventsFromUnfollowedUsers()
    {
        var svc = CreateService();

        // User 1 has NOT followed user 3
        await svc.PublishAsync(userId: 3, eventType: "post:create", entityId: "99");

        var feed = (await svc.GetFeedAsync(userId: 1, page: 1, pageSize: 20)).ToList();

        feed.All(e => e.UserId != 3).Should().BeTrue();
    }

    [Fact]
    public async Task FollowAsync_CreatesFollowRelationship()
    {
        var svc = CreateService();

        await svc.FollowAsync(followerId: 1, followedId: 5);

        await using var db = CreateFreshTenantDb();
        var follow = await db.Follows
            .FirstOrDefaultAsync(f => f.FollowerId == 1 && f.FollowedId == 5);

        follow.Should().NotBeNull();
    }

    [Fact]
    public async Task UnfollowAsync_RemovesFollowRelationship()
    {
        var svc = CreateService();

        await svc.FollowAsync(followerId: 1, followedId: 6);
        await svc.UnfollowAsync(followerId: 1, followedId: 6);

        await using var db = CreateFreshTenantDb();
        var follow = await db.Follows
            .FirstOrDefaultAsync(f => f.FollowerId == 1 && f.FollowedId == 6);

        follow.Should().BeNull();
    }

    [Fact]
    public async Task ReactAsync_AddsReaction_ToActivityEvent()
    {
        var svc = CreateService();

        await svc.PublishAsync(userId: 2, eventType: "post:create", entityId: "50");

        await using var readDb = CreateFreshTenantDb();
        var ev = await readDb.ActivityEvents.FirstAsync(e => e.EntityId == "50");

        await svc.ReactAsync(activityEventId: ev.Id, userId: 1, reactionType: "like");

        await using var verifyDb = CreateFreshTenantDb();
        var reaction = await verifyDb.Reactions
            .FirstOrDefaultAsync(r => r.ActivityEventId == ev.Id && r.UserId == 1);

        reaction.Should().NotBeNull();
        reaction!.ReactionType.Should().Be("like");
    }

    [Fact]
    public async Task ReactAsync_TogglesOff_WhenSameReactionAddedTwice()
    {
        var svc = CreateService();

        await svc.PublishAsync(userId: 2, eventType: "post:create", entityId: "51");

        await using var readDb = CreateFreshTenantDb();
        var ev = await readDb.ActivityEvents.FirstAsync(e => e.EntityId == "51");

        await svc.ReactAsync(ev.Id, userId: 1, reactionType: "like");
        await svc.ReactAsync(ev.Id, userId: 1, reactionType: "like"); // toggle off

        await using var verifyDb = CreateFreshTenantDb();
        var reaction = await verifyDb.Reactions
            .FirstOrDefaultAsync(r => r.ActivityEventId == ev.Id && r.UserId == 1);

        reaction.Should().BeNull();
    }
}

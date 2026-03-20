using Domain.Interfaces;
using FluentAssertions;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests;

/// <summary>
/// Usa il provider InMemory di EF Core.
/// Nota: i metodi che usano ExecuteUpdateAsync (UnfollowAsync)
/// richiedono un DB reale (es. PostgreSQL via Testcontainers)
/// e non sono inclusi qui.
/// </summary>
public class ActivityServiceTests : IDisposable
{
    private readonly BaseAppDbContext _db;
    private readonly ActivityService _svc;

    public ActivityServiceTests()
    {
        var options = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BaseAppDbContext(options);

        var mockFactory = new Mock<ITenantDbContextFactory>();
        mockFactory.Setup(f => f.Create()).Returns(_db);

        var mockCtx = new Mock<ITenantContext>();
        mockCtx.Setup(c => c.TenantId).Returns("tenant-test");

        _svc = new ActivityService(
            mockFactory.Object,
            mockCtx.Object,
            NullLogger<ActivityService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<ApplicationUser> CreateUserAsync(int id, string username, string email)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = username,
            Email = email,
            NormalizedEmail = email.ToUpper(),
            NormalizedUserName = username.ToUpper(),
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // ── PublishAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SavesActivityEvent_ToDatabase()
    {
        await CreateUserAsync(1, "alice", "alice@test.com");

        await _svc.PublishAsync(1, "post:create",
            entityId: "99", entityType: "Post",
            payload: new { text = "Ciao!" });

        var saved = await _db.ActivityEvents.IgnoreQueryFilters().FirstAsync();
        saved.UserId.Should().Be(1);
        saved.EventType.Should().Be("post:create");
        saved.EntityId.Should().Be("99");
        saved.EntityType.Should().Be("Post");
        saved.Payload.Should().Contain("Ciao!");
        saved.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_SavesNullPayload_WhenNoPayloadProvided()
    {
        await CreateUserAsync(2, "bob", "bob@test.com");

        await _svc.PublishAsync(2, "login");

        var saved = await _db.ActivityEvents.IgnoreQueryFilters().FirstAsync();
        saved.Payload.Should().BeNull();
    }

    // ── FollowAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FollowAsync_CreatesUserFollow_Relationship()
    {
        await CreateUserAsync(10, "follower", "follower@test.com");
        await CreateUserAsync(11, "followed", "followed@test.com");

        await _svc.FollowAsync(10, 11);

        var follow = await _db.UserFollows
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.FollowerId == 10 && f.FollowedId == 11);

        follow.Should().NotBeNull();
        follow!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task FollowAsync_ReactivatesExistingFollow_WhenPreviouslyDeactivated()
    {
        await CreateUserAsync(20, "u20", "u20@test.com");
        await CreateUserAsync(21, "u21", "u21@test.com");

        _db.UserFollows.Add(new Domain.Entities.UserFollow
        {
            FollowerId = 20, FollowedId = 21, IsActive = false
        });
        await _db.SaveChangesAsync();

        await _svc.FollowAsync(20, 21);

        var follow = await _db.UserFollows
            .IgnoreQueryFilters()
            .SingleAsync(f => f.FollowerId == 20 && f.FollowedId == 21);
        follow.IsActive.Should().BeTrue();
    }

    // ── ReactAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReactAsync_AddsReaction_WhenNotExists()
    {
        await CreateUserAsync(40, "u40", "u40@test.com");
        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            Id = 100, UserId = 40, EventType = "post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.ReactAsync(100, 40, "like");

        var reaction = await _db.ActivityReactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EventId == 100 && r.UserId == 40);

        reaction.Should().NotBeNull();
        reaction!.ReactionType.Should().Be("like");
    }

    [Fact]
    public async Task ReactAsync_RemovesReaction_Toggle_WhenAlreadyReacted()
    {
        await CreateUserAsync(50, "u50", "u50@test.com");
        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            Id = 200, UserId = 50, EventType = "post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.ActivityReactions.Add(new Domain.Entities.ActivityReaction
        {
            EventId = 200, UserId = 50, ReactionType = "like"
        });
        await _db.SaveChangesAsync();

        await _svc.ReactAsync(200, 50, "like");

        var reaction = await _db.ActivityReactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EventId == 200 && r.UserId == 50);

        reaction.Should().BeNull();
    }

    // ── GetFeedAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFeedAsync_IncludesOwnActivities()
    {
        await CreateUserAsync(60, "u60", "u60@test.com");
        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            UserId = 60, EventType = "own:post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var feed = await _svc.GetFeedAsync(60, page: 1, pageSize: 20);

        feed.Should().ContainSingle(e => e.EventType == "own:post");
    }

    [Fact]
    public async Task GetFeedAsync_IncludesActivitiesOfFollowedUsers()
    {
        await CreateUserAsync(70, "u70", "u70@test.com");
        await CreateUserAsync(71, "u71", "u71@test.com");

        _db.UserFollows.Add(new Domain.Entities.UserFollow
        {
            FollowerId = 70, FollowedId = 71, IsActive = true
        });
        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            UserId = 71, EventType = "followed:post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var feed = await _svc.GetFeedAsync(70, page: 1, pageSize: 20);

        feed.Should().ContainSingle(e => e.EventType == "followed:post");
    }

    [Fact]
    public async Task GetFeedAsync_ExcludesActivitiesOfNonFollowedUsers()
    {
        await CreateUserAsync(80, "u80", "u80@test.com");
        await CreateUserAsync(81, "u81", "u81@test.com");

        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            UserId = 81, EventType = "stranger:post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var feed = await _svc.GetFeedAsync(80, page: 1, pageSize: 20);

        feed.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeedAsync_ReturnsHasReacted_True_WhenUserReacted()
    {
        await CreateUserAsync(90, "u90", "u90@test.com");
        _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
        {
            Id = 300, UserId = 90, EventType = "post", IsPublic = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.ActivityReactions.Add(new Domain.Entities.ActivityReaction
        {
            EventId = 300, UserId = 90, ReactionType = "like"
        });
        await _db.SaveChangesAsync();

        var feed = await _svc.GetFeedAsync(90, page: 1, pageSize: 20);

        feed.Single().HasReacted.Should().BeTrue();
        feed.Single().ReactionsCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFeedAsync_IsPaginated()
    {
        await CreateUserAsync(95, "u95", "u95@test.com");

        for (int i = 1; i <= 5; i++)
            _db.ActivityEvents.Add(new Domain.Entities.ActivityEvent
            {
                UserId = 95, EventType = $"post:{i}", IsPublic = true,
                CreatedAt = DateTime.UtcNow.AddSeconds(-i)
            });
        await _db.SaveChangesAsync();

        var page1 = await _svc.GetFeedAsync(95, page: 1, pageSize: 3);
        var page2 = await _svc.GetFeedAsync(95, page: 2, pageSize: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }
}

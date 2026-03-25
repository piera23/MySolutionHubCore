using Domain.Entities;
using FluentAssertions;
using Infrastructure.Hubs;
using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Security / isolation tests: verifica che i dati di un tenant non siano
/// mai accessibili da un altro tenant. Ogni test usa due database PostgreSQL
/// reali (Tenant A e Tenant B) avviati via Testcontainers.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Security")]
public class TenantIsolationTests : MultiTenantTestBase
{
    // ── Helper factories ──────────────────────────────────────────────────────

    private IActivityService CreateActivityService(TenantContext ctx, string connString)
    {
        var factory = new DirectTenantDbContextFactory(connString);
        var logger  = GetService<ILoggerFactory>().CreateLogger<ActivityService>();
        return new ActivityService(factory, ctx, logger);
    }

    private IChatService CreateChatService(TenantContext ctx, string connString)
    {
        var factory = new DirectTenantDbContextFactory(connString);

        var mockClients = new Mock<IHubClients<IChatHub>>();
        mockClients.Setup(c => c.Group(It.IsAny<string>()))
                   .Returns(new Mock<IChatHub>().Object);

        var mockHub = new Mock<IHubContext<ChatHub, IChatHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var logger = GetService<ILoggerFactory>().CreateLogger<ChatService>();
        return new ChatService(factory, ctx, mockHub.Object, logger);
    }

    // ── 1. ActivityEvents isolation ───────────────────────────────────────────

    [Fact]
    public async Task ActivityEvents_AreIsolatedBetweenTenants()
    {
        var svcA = CreateActivityService(TenantCtx, TenantConnectionString);
        var svcB = CreateActivityService(TenantBCtx, TenantBConnectionString);

        // Tenant A pubblica un'attività
        await svcA.PublishAsync(userId: 1, eventType: "post.created", payload: new { text = "Hello from A" });

        // Tenant B non deve vedere nessuna attività di Tenant A
        await using var dbB = CreateFreshTenantBDb();
        var eventsInB = await dbB.ActivityEvents.IgnoreQueryFilters().ToListAsync();
        eventsInB.Should().BeEmpty("Tenant B non deve contenere eventi di Tenant A");
    }

    [Fact]
    public async Task ActivityFeed_NeverReturnsDataFromAnotherTenant()
    {
        var svcA = CreateActivityService(TenantCtx, TenantConnectionString);
        var svcB = CreateActivityService(TenantBCtx, TenantBConnectionString);

        // Pubblica in entrambi i tenant
        await svcA.PublishAsync(1, "post.created", payload: new { text = "Tenant A post" });
        await svcB.PublishAsync(1, "post.created", payload: new { text = "Tenant B post" });

        // Il feed di Tenant A non deve contenere dati di Tenant B
        var feedA = (await svcA.GetFeedAsync(userId: 1)).ToList();
        feedA.Should().NotBeEmpty("Tenant A ha pubblicato");
        feedA.All(e => e.Payload?.Contains("Tenant A") == true)
             .Should().BeTrue("il feed di A non deve contenere dati di B");

        // Il feed di Tenant B non deve contenere dati di Tenant A
        var feedB = (await svcB.GetFeedAsync(userId: 1)).ToList();
        feedB.Should().NotBeEmpty("Tenant B ha pubblicato");
        feedB.All(e => e.Payload?.Contains("Tenant B") == true)
             .Should().BeTrue("il feed di B non deve contenere dati di A");
    }

    // ── 2. ChatMessages isolation ─────────────────────────────────────────────

    [Fact]
    public async Task ChatMessages_AreIsolatedBetweenTenants()
    {
        var chatA = CreateChatService(TenantCtx, TenantConnectionString);
        var chatB = CreateChatService(TenantBCtx, TenantBConnectionString);

        // Crea conversazione e messaggio in Tenant A
        var convA = await chatA.GetOrCreateDirectAsync(1, 2);
        await chatA.SendMessageAsync(convA.Id, 1, "Secret message from Tenant A");

        // Tenant B non deve avere messaggi
        await using var dbB = CreateFreshTenantBDb();
        var messagesInB = await dbB.ChatMessages.IgnoreQueryFilters().ToListAsync();
        messagesInB.Should().BeEmpty("Tenant B non deve contenere messaggi di Tenant A");
    }

    [Fact]
    public async Task ChatConversations_AreIsolatedBetweenTenants()
    {
        var chatA = CreateChatService(TenantCtx, TenantConnectionString);

        // Crea conversazione in Tenant A
        await chatA.GetOrCreateDirectAsync(1, 2);

        // Tenant B non deve avere conversazioni
        await using var dbB = CreateFreshTenantBDb();
        var convsInB = await dbB.ChatConversations.IgnoreQueryFilters().ToListAsync();
        convsInB.Should().BeEmpty("Tenant B non deve contenere conversazioni di Tenant A");
    }

    // ── 3. Follow graph isolation ─────────────────────────────────────────────

    [Fact]
    public async Task UserFollows_AreIsolatedBetweenTenants()
    {
        var svcA = CreateActivityService(TenantCtx, TenantConnectionString);

        // User 1 segue user 2 in Tenant A
        await svcA.FollowAsync(followerId: 1, followedId: 2);

        // Tenant B non deve vedere questo follow
        await using var dbB = CreateFreshTenantBDb();
        var followsInB = await dbB.UserFollows.IgnoreQueryFilters().ToListAsync();
        followsInB.Should().BeEmpty("Tenant B non deve contenere follow di Tenant A");
    }

    // ── 4. Outbox isolation ───────────────────────────────────────────────────

    [Fact]
    public async Task OutboxMessages_AreIsolatedBetweenTenants()
    {
        var svcA = CreateActivityService(TenantCtx, TenantConnectionString);

        // PublishAsync scrive nel DB di Tenant A + un OutboxMessage
        await svcA.PublishAsync(1, "post.created");

        // L'outbox di Tenant A deve avere un messaggio
        await using var dbA = CreateFreshTenantDb();
        var outboxA = await dbA.OutboxMessages.ToListAsync();
        outboxA.Should().NotBeEmpty("PublishAsync deve scrivere nell'outbox di Tenant A");

        // L'outbox di Tenant B deve essere vuota
        await using var dbB = CreateFreshTenantBDb();
        var outboxB = await dbB.OutboxMessages.ToListAsync();
        outboxB.Should().BeEmpty("Tenant B non deve contenere messaggi outbox di Tenant A");
    }

    // ── 5. TenantContext routing ──────────────────────────────────────────────

    [Fact]
    public async Task TenantContext_RoutesQueries_ToCorrectDatabase()
    {
        // Scrivi un'attività direttamente nel DB di Tenant A
        await using var dbA = CreateFreshTenantDb();
        dbA.ActivityEvents.Add(new ActivityEvent
        {
            UserId    = 99,
            EventType = "marker.tenantA",
            IsPublic  = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbA.SaveChangesAsync();

        // Verifica che il DB di Tenant B non contenga quell'evento
        await using var dbB = CreateFreshTenantBDb();
        var found = await dbB.ActivityEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EventType == "marker.tenantA");

        found.Should().BeFalse(
            "le query sul DB di Tenant B non devono mai restituire dati di Tenant A");
    }
}

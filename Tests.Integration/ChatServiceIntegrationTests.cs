using Application.Interfaces;
using FluentAssertions;
using Infrastructure.Hubs;
using Infrastructure.MultiTenant;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Integration tests for ChatService using a real PostgreSQL database
/// (spun up via Testcontainers). These tests exercise the actual EF Core
/// queries including ExecuteUpdateAsync which is not supported by InMemory.
/// </summary>
[Trait("Category", "Integration")]
public class ChatServiceIntegrationTests : IntegrationTestBase
{
    private IChatService CreateService()
    {
        var factory = new DirectTenantDbContextFactory(TenantConnectionString);

        var mockClients = new Mock<IHubClients<IChatHub>>();
        mockClients
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns(new Mock<IChatHub>().Object);
        mockClients
            .Setup(c => c.User(It.IsAny<string>()))
            .Returns(new Mock<IChatHub>().Object);

        var mockHub = new Mock<IHubContext<ChatHub, IChatHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var logger = GetService<ILoggerFactory>().CreateLogger<ChatService>();
        return new ChatService(factory, TenantCtx, mockHub.Object, logger);
    }

    [Fact]
    public async Task GetOrCreateDirectAsync_CreatesDmConversation_WhenNotExists()
    {
        var svc = CreateService();

        var result = await svc.GetOrCreateDirectAsync(userId1: 1, userId2: 2);

        result.Should().NotBeNull();
        result.IsGroup.Should().BeFalse();
        result.Participants.Should().HaveCount(2);
        result.Participants.Select(p => p.UserId).Should().Contain([1, 2]);
    }

    [Fact]
    public async Task GetOrCreateDirectAsync_ReturnsExistingConversation_WhenAlreadyExists()
    {
        var svc = CreateService();

        var first  = await svc.GetOrCreateDirectAsync(1, 2);
        var second = await svc.GetOrCreateDirectAsync(1, 2);

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task CreateGroupAsync_CreatesGroupConversation()
    {
        var svc = CreateService();

        var result = await svc.CreateGroupAsync(
            title: "Team Alpha",
            creatorUserId: 1,
            participantIds: [2, 3]);

        result.Should().NotBeNull();
        result.IsGroup.Should().BeTrue();
        result.Title.Should().Be("Team Alpha");
        result.Participants.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task SendMessageAsync_PersistsMessage_AndReturnsDto()
    {
        var svc = CreateService();
        var conv = await svc.GetOrCreateDirectAsync(1, 2);

        var msg = await svc.SendMessageAsync(
            conversationId: conv.Id,
            senderId: 1,
            body: "Hello integration test!");

        msg.Should().NotBeNull();
        msg.Body.Should().Be("Hello integration test!");
        msg.SenderId.Should().Be(1);
        msg.SentAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessages_InDescendingOrder()
    {
        var svc = CreateService();
        var conv = await svc.GetOrCreateDirectAsync(1, 2);

        await svc.SendMessageAsync(conv.Id, 1, "First");
        await svc.SendMessageAsync(conv.Id, 2, "Second");
        await svc.SendMessageAsync(conv.Id, 1, "Third");

        var messages = (await svc.GetMessagesAsync(conv.Id, userId: 1, page: 1, pageSize: 10))
            .ToList();

        messages.Should().HaveCount(3);
        // Newest first
        messages[0].Body.Should().Be("Third");
        messages[2].Body.Should().Be("First");
    }

    [Fact]
    public async Task GetUserConversationsAsync_ReturnsOnlyUserConversations()
    {
        var svc = CreateService();

        await svc.GetOrCreateDirectAsync(1, 2);   // user 1 and 2
        await svc.GetOrCreateDirectAsync(3, 4);   // user 3 and 4 — user 1 must NOT see this

        var conversations = (await svc.GetUserConversationsAsync(
            userId: 1, page: 1, pageSize: 20)).ToList();

        conversations.Should().NotBeEmpty();
        conversations.All(c => c.Participants.Any(p => p.UserId == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkConversationReadAsync_UpdatesUnreadCount_ToZero()
    {
        var svc  = CreateService();
        var conv = await svc.GetOrCreateDirectAsync(1, 2);

        await svc.SendMessageAsync(conv.Id, 2, "Unread message 1");
        await svc.SendMessageAsync(conv.Id, 2, "Unread message 2");

        // Mark as read by user 1
        await svc.MarkConversationReadAsync(conv.Id, userId: 1);

        var conversations = (await svc.GetUserConversationsAsync(
            userId: 1, page: 1, pageSize: 20)).ToList();

        var updated = conversations.FirstOrDefault(c => c.Id == conv.Id);
        updated.Should().NotBeNull();
        updated!.UnreadCount.Should().Be(0);
    }
}

/// <summary>
/// Simple ITenantDbContextFactory that always returns a context for the given
/// connection string, bypassing the HTTP-scoped TenantContext.
/// </summary>
internal sealed class DirectTenantDbContextFactory : ITenantDbContextFactory
{
    private readonly string _connectionString;

    public DirectTenantDbContextFactory(string connectionString)
        => _connectionString = connectionString;

    public Microsoft.EntityFrameworkCore.DbContext Create()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder()
            .UseNpgsql(_connectionString)
            .Options;
        return new BaseAppDbContext(options);
    }
}

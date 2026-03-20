using Application.Interfaces;
using Domain.Interfaces;
using FluentAssertions;
using Infrastructure.Hubs;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests;

/// <summary>
/// Usa il provider InMemory di EF Core.
/// Nota: SendMessageAsync usa ExecuteUpdateAsync per LastMessageAt —
/// il messaggio viene comunque salvato, ma il campo LastMessageAt della
/// conversazione non viene aggiornato con InMemory (richiede PostgreSQL).
/// </summary>
public class ChatServiceTests : IDisposable
{
    private readonly BaseAppDbContext _db;
    private readonly ChatService _svc;

    public ChatServiceTests()
    {
        var options = new DbContextOptionsBuilder<BaseAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BaseAppDbContext(options);

        var mockFactory = new Mock<ITenantDbContextFactory>();
        mockFactory.Setup(f => f.Create()).Returns(_db);

        var mockCtx = new Mock<ITenantContext>();
        mockCtx.Setup(c => c.TenantId).Returns("tenant-chat");

        var mockChatHubClients = new Mock<IChatHub>();
        mockChatHubClients.Setup(h => h.ReceiveMessage(It.IsAny<ChatMessageDto>()))
            .Returns(Task.CompletedTask);

        var mockGroupManager = new Mock<IGroupManager>();
        mockGroupManager.Setup(g =>
            g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        var mockHubClients = new Mock<IHubClients<IChatHub>>();
        mockHubClients.Setup(c => c.Group(It.IsAny<string>()))
            .Returns(mockChatHubClients.Object);

        var mockHub = new Mock<IHubContext<ChatHub, IChatHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockHubClients.Object);
        mockHub.Setup(h => h.Groups).Returns(mockGroupManager.Object);

        _svc = new ChatService(
            mockFactory.Object,
            mockCtx.Object,
            mockHub.Object,
            NullLogger<ChatService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<ApplicationUser> CreateUserAsync(int id, string username)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = username,
            Email = $"{username}@test.com",
            NormalizedEmail = $"{username}@test.com".ToUpper(),
            NormalizedUserName = username.ToUpper(),
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // ── GetOrCreateDirectAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateDirectAsync_CreatesNewConversation_WhenNoneExists()
    {
        await CreateUserAsync(1, "alice");
        await CreateUserAsync(2, "bob");

        var conv = await _svc.GetOrCreateDirectAsync(1, 2);

        conv.Should().NotBeNull();
        conv.IsGroup.Should().BeFalse();
        conv.Participants.Should().HaveCount(2);
        conv.Participants.Select(p => p.UserId).Should().Contain([1, 2]);
    }

    [Fact]
    public async Task GetOrCreateDirectAsync_ReturnsExistingConversation_OnSecondCall()
    {
        await CreateUserAsync(3, "carol");
        await CreateUserAsync(4, "dave");

        var first = await _svc.GetOrCreateDirectAsync(3, 4);
        var second = await _svc.GetOrCreateDirectAsync(3, 4);

        second.Id.Should().Be(first.Id);
        _db.ChatConversations.Count().Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateDirectAsync_ShowsOtherUserAsTitle_ForDirectChat()
    {
        await CreateUserAsync(5, "elena");
        await CreateUserAsync(6, "franco");

        var conv = await _svc.GetOrCreateDirectAsync(5, 6);

        conv.Title.Should().Be("franco");
    }

    // ── CreateGroupAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroupAsync_CreatesGroupConversation_WithTitle()
    {
        await CreateUserAsync(10, "g1");
        await CreateUserAsync(11, "g2");
        await CreateUserAsync(12, "g3");

        var conv = await _svc.CreateGroupAsync(10, "Team Alpha", [10, 11, 12]);

        conv.IsGroup.Should().BeTrue();
        conv.Title.Should().Be("Team Alpha");
        conv.Participants.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateGroupAsync_AssignsAdmin_ToCreator()
    {
        await CreateUserAsync(20, "creator");
        await CreateUserAsync(21, "member1");
        await CreateUserAsync(22, "member2");

        var conv = await _svc.CreateGroupAsync(20, "My Group", [20, 21, 22]);

        var participants = await _db.ChatParticipants
            .IgnoreQueryFilters()
            .Where(p => p.ConversationId == conv.Id)
            .ToListAsync();

        participants.Single(p => p.UserId == 20).Role
            .Should().Be(Domain.Entities.ParticipantRole.Admin);
        participants.Where(p => p.UserId != 20)
            .All(p => p.Role == Domain.Entities.ParticipantRole.Member)
            .Should().BeTrue();
    }

    [Fact]
    public async Task CreateGroupAsync_AutoAddsCreator_WhenNotInMemberList()
    {
        await CreateUserAsync(30, "boss");
        await CreateUserAsync(31, "m1");

        var conv = await _svc.CreateGroupAsync(30, "Implicit Group", [31]);

        conv.Participants.Select(p => p.UserId).Should().Contain(30);
    }

    // ── GetUserConversationsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetUserConversationsAsync_ReturnsOnlyUserConversations()
    {
        await CreateUserAsync(40, "u40");
        await CreateUserAsync(41, "u41");
        await CreateUserAsync(42, "u42");

        await _svc.GetOrCreateDirectAsync(40, 41);
        await _svc.GetOrCreateDirectAsync(41, 42); // u40 non partecipa

        var convs = await _svc.GetUserConversationsAsync(40);

        convs.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUserConversationsAsync_IsPaginated()
    {
        await CreateUserAsync(50, "u50");
        for (int i = 51; i <= 55; i++)
        {
            await CreateUserAsync(i, $"u{i}");
            await _svc.GetOrCreateDirectAsync(50, i);
        }

        var page1 = await _svc.GetUserConversationsAsync(50, page: 1, pageSize: 3);
        var page2 = await _svc.GetUserConversationsAsync(50, page: 2, pageSize: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    // ── GetMessagesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessages_ForConversation()
    {
        await CreateUserAsync(60, "u60");
        await CreateUserAsync(61, "u61");

        var conv = await _svc.GetOrCreateDirectAsync(60, 61);

        _db.ChatMessages.Add(new Domain.Entities.ChatMessage
        {
            ConversationId = conv.Id, SenderId = 60,
            Body = "Ciao!", SentAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var messages = await _svc.GetMessagesAsync(conv.Id, 60);

        messages.Should().ContainSingle(m => m.Body == "Ciao!");
        messages.Single().IsOwn.Should().BeTrue();
    }

    [Fact]
    public async Task GetMessagesAsync_IsOwn_FalseForOtherSender()
    {
        await CreateUserAsync(70, "u70");
        await CreateUserAsync(71, "u71");

        var conv = await _svc.GetOrCreateDirectAsync(70, 71);

        _db.ChatMessages.Add(new Domain.Entities.ChatMessage
        {
            ConversationId = conv.Id, SenderId = 71,
            Body = "Risposta di u71", SentAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var messages = await _svc.GetMessagesAsync(conv.Id, 70);

        messages.Single().IsOwn.Should().BeFalse();
        messages.Single().SenderUsername.Should().Be("u71");
    }

    // ── MarkConversationReadAsync ─────────────────────────────────────────────

    [Fact]
    public async Task MarkConversationReadAsync_CreatesReadStatuses_ForUnreadMessages()
    {
        await CreateUserAsync(90, "u90");
        await CreateUserAsync(91, "u91");

        var conv = await _svc.GetOrCreateDirectAsync(90, 91);

        _db.ChatMessages.AddRange(
            new Domain.Entities.ChatMessage
            {
                ConversationId = conv.Id, SenderId = 91,
                Body = "Msg1", SentAt = DateTime.UtcNow
            },
            new Domain.Entities.ChatMessage
            {
                ConversationId = conv.Id, SenderId = 91,
                Body = "Msg2", SentAt = DateTime.UtcNow
            }
        );
        await _db.SaveChangesAsync();

        await _svc.MarkConversationReadAsync(conv.Id, 90);

        var readCount = await _db.MessageReadStatuses
            .Where(r => r.UserId == 90)
            .CountAsync();

        readCount.Should().Be(2);
    }

    [Fact]
    public async Task MarkConversationReadAsync_IsIdempotent_WhenAlreadyRead()
    {
        await CreateUserAsync(92, "u92");
        await CreateUserAsync(93, "u93");

        var conv = await _svc.GetOrCreateDirectAsync(92, 93);

        _db.ChatMessages.Add(new Domain.Entities.ChatMessage
        {
            ConversationId = conv.Id, SenderId = 93,
            Body = "Leggi due volte", SentAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.MarkConversationReadAsync(conv.Id, 92);
        await _svc.MarkConversationReadAsync(conv.Id, 92);

        var readCount = await _db.MessageReadStatuses
            .Where(r => r.UserId == 92)
            .CountAsync();
        readCount.Should().Be(1);
    }
}

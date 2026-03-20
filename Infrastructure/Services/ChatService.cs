using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Hubs;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class ChatService : IChatService
    {
        private readonly ITenantDbContextFactory _dbFactory;
        private readonly ITenantContext _tenantContext;
        private readonly IHubContext<ChatHub, IChatHub> _chatHub;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            ITenantDbContextFactory dbFactory,
            ITenantContext tenantContext,
            IHubContext<ChatHub, IChatHub> chatHub,
            ILogger<ChatService> logger)
        {
            _dbFactory = dbFactory;
            _tenantContext = tenantContext;
            _chatHub = chatHub;
            _logger = logger;
        }

        public async Task<ConversationDto> GetOrCreateDirectAsync(
            int userId1, int userId2, CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            // Cerca conversazione 1-a-1 esistente
            var existing = await db.ChatConversations
                .Where(c => !c.IsGroup &&
                    c.Participants.Any(p => p.UserId == userId1 && p.IsActive) &&
                    c.Participants.Any(p => p.UserId == userId2 && p.IsActive))
                .Include(c => c.Participants)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
                return await MapConversationAsync(existing, userId1, db, ct);

            // Crea nuova conversazione diretta
            var conversation = new ChatConversation
            {
                IsGroup = false,
                CreatedAt = DateTime.UtcNow
            };

            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync(ct);

            db.ChatParticipants.AddRange(
                new ChatParticipant { ConversationId = conversation.Id, UserId = userId1, Role = ParticipantRole.Member },
                new ChatParticipant { ConversationId = conversation.Id, UserId = userId2, Role = ParticipantRole.Member }
            );

            await db.SaveChangesAsync(ct);

            await db.Entry(conversation).Collection(c => c.Participants).LoadAsync(ct);
            return await MapConversationAsync(conversation, userId1, db, ct);
        }

        public async Task<ConversationDto> CreateGroupAsync(
            int creatorId, string title, IEnumerable<int> memberIds, CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var conversation = new ChatConversation
            {
                Title = title,
                IsGroup = true,
                CreatedAt = DateTime.UtcNow
            };

            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync(ct);

            var participants = memberIds
                .Distinct()
                .Select(uid => new ChatParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = uid,
                    Role = uid == creatorId ? ParticipantRole.Admin : ParticipantRole.Member
                }).ToList();

            // Assicurati che il creatore sia incluso
            if (!participants.Any(p => p.UserId == creatorId))
                participants.Add(new ChatParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = creatorId,
                    Role = ParticipantRole.Admin
                });

            db.ChatParticipants.AddRange(participants);
            await db.SaveChangesAsync(ct);

            await db.Entry(conversation).Collection(c => c.Participants).LoadAsync(ct);
            return await MapConversationAsync(conversation, creatorId, db, ct);
        }

        public async Task<IEnumerable<ConversationDto>> GetUserConversationsAsync(
            int userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            pageSize = Math.Clamp(pageSize, 1, 50);
            page = Math.Max(page, 1);

            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var conversations = await db.ChatConversations
                .Where(c => c.Participants.Any(p => p.UserId == userId && p.IsActive))
                .Include(c => c.Participants)
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var result = new List<ConversationDto>();
            foreach (var conv in conversations)
                result.Add(await MapConversationAsync(conv, userId, db, ct));

            return result;
        }

        public async Task<IEnumerable<ChatMessageDto>> GetMessagesAsync(
            int conversationId, int userId,
            int page = 1, int pageSize = 30,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var messages = await db.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(db.Users,
                    m => m.SenderId,
                    u => u.Id,
                    (m, u) => new { Message = m, User = u })
                .ToListAsync(ct);

            return messages
                .OrderBy(x => x.Message.SentAt)
                .Select(x => new ChatMessageDto(
                    Id: x.Message.Id,
                    ConversationId: x.Message.ConversationId,
                    SenderId: x.Message.SenderId,
                    SenderUsername: x.User.UserName ?? "",
                    SenderAvatar: x.User.AvatarUrl,
                    Body: x.Message.Body,
                    SentAt: x.Message.SentAt,
                    IsOwn: x.Message.SenderId == userId
                ));
        }

        public async Task<ChatMessageDto> SendMessageAsync(
            int conversationId, int senderId,
            string body, string? attachmentUrl = null,
            CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var message = new ChatMessage
            {
                ConversationId = conversationId,
                SenderId = senderId,
                Body = body,
                AttachmentUrl = attachmentUrl,
                SentAt = DateTime.UtcNow
            };

            db.ChatMessages.Add(message);

            // Aggiorna LastMessageAt sulla conversazione
            await db.ChatConversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.LastMessageAt, DateTime.UtcNow), ct);

            await db.SaveChangesAsync(ct);

            var sender = await db.Users.FindAsync(new object[] { senderId }, ct);

            var dto = new ChatMessageDto(
                Id: message.Id,
                ConversationId: conversationId,
                SenderId: senderId,
                SenderUsername: sender?.UserName ?? "",
                SenderAvatar: sender?.AvatarUrl,
                Body: body,
                SentAt: message.SentAt,
                IsOwn: false
            );

            // Invia in real-time a tutti nella conversazione
            var groupName = $"{_tenantContext.TenantId}:conv:{conversationId}";
            await _chatHub.Clients.Group(groupName).ReceiveMessage(dto);

            return dto;
        }

        public async Task MarkConversationReadAsync(
            int conversationId, int userId, CancellationToken ct = default)
        {
            await using var db = (BaseAppDbContext)_dbFactory.Create();

            var unreadIds = await db.ChatMessages
                .Where(m => m.ConversationId == conversationId &&
                            !m.ReadStatuses.Any(r => r.UserId == userId))
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (!unreadIds.Any()) return;

            db.MessageReadStatuses.AddRange(unreadIds.Select(id => new MessageReadStatus
            {
                MessageId = id,
                UserId = userId,
                ReadAt = DateTime.UtcNow
            }));

            await db.SaveChangesAsync(ct);
        }

        // ── Helper ────────────────────────────────────────────────
        private async Task<ConversationDto> MapConversationAsync(
            ChatConversation conv, int currentUserId,
            BaseAppDbContext db, CancellationToken ct)
        {
            var participantIds = conv.Participants.Select(p => p.UserId).ToList();

            var users = await db.Users
                .Where(u => participantIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct);

            var lastMessage = await db.ChatMessages
                .Where(m => m.ConversationId == conv.Id)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync(ct);

            var unreadCount = await db.ChatMessages
                .Where(m => m.ConversationId == conv.Id &&
                            !m.ReadStatuses.Any(r => r.UserId == currentUserId))
                .CountAsync(ct);

            var participants = conv.Participants
                .Where(p => p.IsActive)
                .Select(p => new ParticipantDto(
                    UserId: p.UserId,
                    Username: users.GetValueOrDefault(p.UserId)?.UserName ?? "",
                    AvatarUrl: users.GetValueOrDefault(p.UserId)?.AvatarUrl,
                    Role: p.Role.ToString()
                ));

            // Per chat 1-a-1, usa il nome dell'altro utente come titolo
            var title = conv.IsGroup
                ? conv.Title
                : users.Values
                    .FirstOrDefault(u => u.Id != currentUserId)?.UserName;

            return new ConversationDto(
                Id: conv.Id,
                Title: title,
                IsGroup: conv.IsGroup,
                LastMessageAt: conv.LastMessageAt,
                LastMessageBody: lastMessage?.Body,
                UnreadCount: unreadCount,
                Participants: participants
            );
        }
    }
}

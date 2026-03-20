using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IChatService
    {
        Task<ConversationDto> GetOrCreateDirectAsync(int userId1, int userId2, CancellationToken ct = default);
        Task<ConversationDto> CreateGroupAsync(int creatorId, string title, IEnumerable<int> memberIds, CancellationToken ct = default);
        Task<IEnumerable<ConversationDto>> GetUserConversationsAsync(int userId, int page = 1, int pageSize = 20, CancellationToken ct = default);
        Task<IEnumerable<ChatMessageDto>> GetMessagesAsync(int conversationId, int userId, int page = 1, int pageSize = 30, CancellationToken ct = default);
        Task<ChatMessageDto> SendMessageAsync(int conversationId, int senderId, string body, string? attachmentUrl = null, CancellationToken ct = default);
        Task MarkConversationReadAsync(int conversationId, int userId, CancellationToken ct = default);
    }

    public record ConversationDto(
        int Id,
        string? Title,
        bool IsGroup,
        DateTime? LastMessageAt,
        string? LastMessageBody,
        int UnreadCount,
        IEnumerable<ParticipantDto> Participants
    );

    public record ParticipantDto(
        int UserId,
        string Username,
        string? AvatarUrl,
        string Role
    );
}

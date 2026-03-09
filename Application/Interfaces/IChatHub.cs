using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IChatHub
    {
        Task ReceiveMessage(ChatMessageDto message);
        Task UserTyping(TypingDto typing);
        Task MessageRead(MessageReadDto read);
        Task UserOnline(int userId);
        Task UserOffline(int userId);
    }

    public record ChatMessageDto(
        int Id,
        int ConversationId,
        int SenderId,
        string SenderUsername,
        string? SenderAvatar,
        string Body,
        DateTime SentAt,
        bool IsOwn
    );

    public record TypingDto(
        int ConversationId,
        int UserId,
        string Username,
        bool IsTyping
    );

    public record MessageReadDto(
        int MessageId,
        int ConversationId,
        int UserId,
        DateTime ReadAt
    );
}

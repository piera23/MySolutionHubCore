using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ChatConversation : BaseEntity
    {
        public string? Title { get; set; }                   // null per chat 1-a-1
        public bool IsGroup { get; set; } = false;
        public DateTime? LastMessageAt { get; set; }

        public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatParticipant : BaseEntity
    {
        public int ConversationId { get; set; }
        public int UserId { get; set; }
        public ParticipantRole Role { get; set; } = ParticipantRole.Member;
        public DateTime? LeftAt { get; set; }
        public bool IsActive { get; set; } = true;

        public ChatConversation Conversation { get; set; } = null!;
    }

    public class ChatMessage : BaseEntity
    {
        public int ConversationId { get; set; }
        public int SenderId { get; set; }
        public string Body { get; set; } = string.Empty;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public string? AttachmentUrl { get; set; }

        public ChatConversation Conversation { get; set; } = null!;
        public ICollection<MessageReadStatus> ReadStatuses { get; set; } = new List<MessageReadStatus>();
    }

    public class MessageReadStatus : BaseEntity
    {
        public int MessageId { get; set; }
        public int UserId { get; set; }
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;

        public ChatMessage Message { get; set; } = null!;
    }

    public enum ParticipantRole
    {
        Member = 0,
        Admin = 1
    }
}

using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ActivityEvent : BaseEntity
    {
        public int UserId { get; set; }                      // chi ha fatto l'azione
        public string EventType { get; set; } = string.Empty; // es. "order.created"
        public string? EntityId { get; set; }
        public string? EntityType { get; set; }
        public string? Payload { get; set; }                 // JSON con dati extra
        public bool IsPublic { get; set; } = true;           // visibile nel feed globale
    }

    public class UserFollow : BaseEntity
    {
        public int FollowerId { get; set; }                  // chi segue
        public int FollowedId { get; set; }                  // chi viene seguito
        public bool IsActive { get; set; } = true;
    }

    public class ActivityReaction : BaseEntity
    {
        public int EventId { get; set; }
        public int UserId { get; set; }
        public string ReactionType { get; set; } = "like";
    }
}

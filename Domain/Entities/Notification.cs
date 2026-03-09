using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Notification : BaseEntity
    {
        public int UserId { get; set; }
        public string Type { get; set; } = string.Empty;     // es. "mention", "assignment"
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? EntityId { get; set; }                // id dell'entità collegata
        public string? EntityType { get; set; }              // es. "Order", "Task"
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        public int? SenderId { get; set; }                   // chi ha generato la notifica
    }
}

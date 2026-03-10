using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Entities
{
    public class TenantMigrationLog
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string MigrationId { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; }
        public MigrationStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum MigrationStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }
}

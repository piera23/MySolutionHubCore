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
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; } = null!;

        public string MigrationId { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public MigrationStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum MigrationStatus
    {
        Success = 0,
        Failed = 1,
        Pending = 2
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Entities
{
    public class TenantConnection
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; } = null!;

        // La connection string viene cifrata a riposo
        public string ConnectionStringEncrypted { get; set; } = string.Empty;
        public string DbVersion { get; set; } = string.Empty;   // es. "1.0.0"
        public string? Region { get; set; }                     // es. "italynorth"
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

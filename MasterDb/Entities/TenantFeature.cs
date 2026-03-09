using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Entities
{
    public class TenantFeature
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; } = null!;

        public string FeatureKey { get; set; } = string.Empty;  // es. "social:chat"
        public bool IsEnabled { get; set; } = false;
        public string? Config { get; set; }                     // JSON opzionale per config extra
    }
}

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
        public string TenantId { get; set; } = string.Empty;
        public string FeatureKey { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string? Config { get; set; }
    }
}

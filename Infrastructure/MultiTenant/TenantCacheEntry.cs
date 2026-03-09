using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.MultiTenant
{
    public class TenantCacheEntry
    {
        public string TenantId { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string ConnectionStringEncrypted { get; set; } = string.Empty;
        public List<string> EnabledFeatures { get; set; } = new();
        public Dictionary<string, string> Settings { get; set; } = new();
    }
}

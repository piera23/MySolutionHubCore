using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterDb.Entities
{
    public class Tenant
    {
        public int Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string Name {  get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            
    }
}

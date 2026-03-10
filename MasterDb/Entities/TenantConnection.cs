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
        public string TenantId { get; set; } = string.Empty;
        public string ConnectionStringEncrypted { get; set; } = string.Empty;
        public string DbVersion { get; set; } = "1.0";
        public string Region { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }
}

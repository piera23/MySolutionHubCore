using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Interfaces
{
    public interface ITenantContext
    {
        string TenantId { get; }
        string TenantName { get; }
        string ConnectionString { get; }
        bool IsFeatureEnabled(string featureKey);
        IReadOnlyDictionary<string, string> Settings { get; }
    }
}

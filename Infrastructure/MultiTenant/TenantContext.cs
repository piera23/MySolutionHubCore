using Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.MultiTenant
{
    public class TenantContext : ITenantContext
    {
        public string TenantId { get; private set; } = string.Empty;
        public string TenantName { get; private set; } = string.Empty;
        public string ConnectionString { get; private set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Settings { get; private set; }
            = new Dictionary<string, string>();

        private readonly HashSet<string> _enabledFeatures = new();

        public bool IsFeatureEnabled(string featureKey)
            => _enabledFeatures.Contains(featureKey);

        public void SetTenant(
            string tenantId,
            string tenantName,
            string connectionString,
            IEnumerable<string> enabledFeatures,
            Dictionary<string, string>? settings = null)
        {
            TenantId = tenantId;
            TenantName = tenantName;
            ConnectionString = connectionString;
            _enabledFeatures.Clear();

            foreach (var f in enabledFeatures)
                _enabledFeatures.Add(f);

            Settings = settings ?? new Dictionary<string, string>();
        }
    }
}

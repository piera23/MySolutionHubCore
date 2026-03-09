using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Interfaces
{
    public interface ITenantResolver
    {
        Task<ITenantContext?> ResolveAsync(string subdomain, CancellationToken ct = default);
    }
}

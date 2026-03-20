using Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.MultiTenant
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantResolutionMiddleware> _logger;

        public TenantResolutionMiddleware(RequestDelegate next,
            ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, TenantContext tenantContext)
        {
            string? identifier = null;

            // 1. Header esplicito (Swagger / chiamate server-side)
            if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
            {
                identifier = headerValue.ToString();
            }
            // 2. Query string (SignalR)
            else if (context.Request.Query.TryGetValue("tenantId", out var queryValue))
            {
                identifier = queryValue.ToString();
            }
            // 3. Host completo senza porta (es. cliente1.localhost o cliente1.miodominio.com)
            else
            {
                var host = context.Request.Host.Host; // senza porta
                if (!string.IsNullOrEmpty(host) && host != "localhost")
                    identifier = host;
            }

            _logger.LogDebug("[Tenant] Identifier: {Identifier}", identifier);

            if (!string.IsNullOrEmpty(identifier))
            {
                try
                {
                    var tenant = await resolver.ResolveAsync(identifier);
                    if (tenant is not null)
                    {
                        _logger.LogInformation("[Tenant] Resolved: {TenantId}", tenant.TenantId);
                        tenantContext.SetTenant(tenant.TenantId, tenant.TenantName, tenant.ConnectionString);
                    }
                    else
                    {
                        _logger.LogWarning("[Tenant] NOT FOUND: {Identifier}", identifier);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore risoluzione tenant: {Identifier}", identifier);
                }
            }

            await _next(context);
        }
    }
}

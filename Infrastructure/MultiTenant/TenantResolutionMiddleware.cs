using Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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

        public async Task InvokeAsync(HttpContext context,
            ITenantResolver resolver,
            TenantContext tenantContext)
        {
            // 1. Prima prova l'header X-Tenant-Id (Swagger / server-side calls)
            var subdomain = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();

            // 2. Se non c'è header, prova a estrarre dal sottodominio
            if (string.IsNullOrEmpty(subdomain))
            {
                subdomain = ExtractSubdomain(context.Request.Host.Host);
            }

            if (!string.IsNullOrEmpty(subdomain))
            {
                try
                {
                    var tenant = await resolver.ResolveAsync(subdomain);
                    if (tenant is not null)
                    {
                        tenantContext.SetTenant(
                            tenant.TenantId,
                            tenant.TenantName,
                            tenant.ConnectionString);

                        _logger.LogDebug(
                            "Tenant risolto: {TenantId} da '{Subdomain}'",
                            tenant.TenantId, subdomain);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Tenant non trovato per subdomain: {Subdomain}", subdomain);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Errore nella risoluzione del tenant: {Subdomain}", subdomain);
                }
            }

            await _next(context);
        }

        private static string? ExtractSubdomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;

            // localhost o 127.0.0.1 — nessun sottodominio
            if (host == "localhost" || host == "127.0.0.1") return null;

            // cliente1.localhost → "cliente1"
            if (host.EndsWith(".localhost"))
                return host[..host.LastIndexOf(".localhost")];

            // cliente1.app.com → "cliente1"
            var parts = host.Split('.');
            if (parts.Length >= 3)
                return parts[0];

            return null;
        }
    }
}

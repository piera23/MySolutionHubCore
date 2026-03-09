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

        public TenantResolutionMiddleware(
            RequestDelegate next,
            ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantResolver resolver,
            TenantContext tenantContext)  // scoped — viene popolato qui
        {
            var host = context.Request.Host.Host; // es. "cliente1.app.com"
            var subdomain = ExtractSubdomain(host);

            if (subdomain is not null)
            {
                var resolved = await resolver.ResolveAsync(subdomain);

                if (resolved is not null)
                {
                    // Popola il TenantContext scoped per questa request
                    tenantContext.SetTenant(
                        resolved.TenantId,
                        resolved.TenantName,
                        resolved.ConnectionString,
                        resolved.Settings.Keys,
                        new Dictionary<string, string>(resolved.Settings));
                }
                else
                {
                    _logger.LogWarning(
                        "Tenant non risolto per host: {Host}", host);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("Tenant non trovato.");
                    return;
                }
            }

            await _next(context);
        }

        private static string? ExtractSubdomain(string host)
        {
            // Rimuovi la porta se presente (es. "cliente1.localhost:5001" → "cliente1.localhost")
            host = host.Split(':')[0];

            var parts = host.Split('.');

            // Produzione: "cliente1.app.com" → "cliente1"
            if (parts.Length >= 3)
                return parts[0];

            // Sviluppo: "cliente1.localhost" → "cliente1"
            if (parts.Length == 2 && parts[1] == "localhost")
                return parts[0];

            return null;
        }
        //private static string? ExtractSubdomain(string host)
        //{
        //    // "cliente1.app.com" → "cliente1"
        //    // "localhost" → null (utile in sviluppo)
        //    var parts = host.Split('.');
        //    return parts.Length >= 3 ? parts[0] : null;
        //}
    }
}

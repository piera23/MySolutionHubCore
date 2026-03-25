using Domain.Interfaces;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        public async Task InvokeAsync(
            HttpContext context,
            ITenantResolver resolver,
            TenantContext tenantContext,
            MasterDbContext masterDb)
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
                        // Verifica scadenza trial
                        var trialSetting = await masterDb.TenantSettings
                            .FirstOrDefaultAsync(s =>
                                s.TenantId == tenant.TenantId && s.Key == "Trial:EndsAt");

                        if (trialSetting is not null &&
                            DateTime.TryParse(trialSetting.Value, out var trialEndsAt) &&
                            trialEndsAt < DateTime.UtcNow)
                        {
                            _logger.LogWarning(
                                "[Tenant] Trial scaduto per {TenantId} (scaduto il {TrialEndsAt}).",
                                tenant.TenantId, trialEndsAt);

                            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "trial_expired",
                                message = "Il periodo di prova è scaduto. Effettua l'upgrade per continuare.",
                                expiredAt = trialEndsAt
                            });
                            return;
                        }

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

using Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Filters
{
    /// <summary>
    /// Blocca la request con 403 se la feature indicata non è abilitata
    /// per il tenant corrente. Restituisce 400 se nessun tenant è stato risolto.
    /// Applicabile su classe (controller) o su singolo metodo.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireFeatureAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _featureKey;

        public RequireFeatureAttribute(string featureKey)
        {
            _featureKey = featureKey;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var tenantContext = context.HttpContext.RequestServices
                .GetRequiredService<ITenantContext>();

            if (string.IsNullOrEmpty(tenantContext.TenantId))
            {
                context.Result = new ObjectResult(
                    new { error = "Nessun tenant risolto per questa request." })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
                return;
            }

            if (!tenantContext.IsFeatureEnabled(_featureKey))
            {
                context.Result = new ObjectResult(new
                {
                    error = $"La feature '{_featureKey}' non è abilitata per il tenant '{tenantContext.TenantId}'."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }
    }
}

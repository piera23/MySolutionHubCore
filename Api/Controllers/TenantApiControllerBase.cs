using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers
{
    /// <summary>
    /// Base controller per tutti gli endpoint che operano nel contesto del tenant corrente.
    /// Fornisce l'helper GetUserId() per ricavare l'id utente dal JWT.
    /// Tutti i controller derivati ereditano automaticamente ApiVersion 1.0.
    /// </summary>
    [ApiVersion("1.0")]
    public abstract class TenantApiControllerBase : ControllerBase
    {
        protected int GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}

using Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;

        public DiagnosticsController(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        [HttpGet("tenant")]
        public IActionResult GetTenantInfo()
        {
            if (string.IsNullOrEmpty(_tenantContext.TenantId))
                return NotFound(new { message = "Nessun tenant risolto per questa request." });

            return Ok(new
            {
                tenantId = _tenantContext.TenantId,
                tenantName = _tenantContext.TenantName,
                features = new
                {
                    feed = _tenantContext.IsFeatureEnabled("social:feed"),
                    chat = _tenantContext.IsFeatureEnabled("social:chat"),
                    notifications = _tenantContext.IsFeatureEnabled("social:notifications")
                }
            });
        }
    }
}

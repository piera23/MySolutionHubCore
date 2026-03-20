using Domain.Interfaces;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize(Roles = "Admin")]
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

        [HttpGet("master-db")]
        public async Task<IActionResult> GetMasterDb([FromServices] MasterDbContext masterDb)
        {
            var tenants = await masterDb.Tenants.ToListAsync();
            var connections = await masterDb.TenantConnections.ToListAsync();
            var features = await masterDb.TenantFeatures.ToListAsync();

            return Ok(new
            {
                Tenants = tenants.Select(t => new
                {
                    t.Id,
                    t.TenantId,
                    t.Subdomain,
                    t.Name,
                    t.IsActive
                }),
                Connections = connections.Select(c => new
                {
                    c.Id,
                    c.TenantId,
                    c.Region,
                    c.DbVersion
                }),
                Features = features.Select(f => new
                {
                    f.Id,
                    f.TenantId,
                    f.FeatureKey,
                    f.IsEnabled
                })
            });
        }

        [HttpPost("fix-tenant")]
        public async Task<IActionResult> FixTenant(
    [FromServices] MasterDbContext masterDb,
    [FromServices] ITenantEncryption encryption)
        {
            var connExists = await masterDb.TenantConnections
                .AnyAsync(c => c.TenantId == "tenant001");

            if (!connExists)
            {
                masterDb.TenantConnections.Add(new MasterDb.Entities.TenantConnection
                {
                    TenantId = "tenant001",
                    ConnectionStringEncrypted = encryption.Encrypt("Data Source=tenant001.sqlite"),
                    DbVersion = "1.0.0",
                    Region = "eu-west",
                    UpdatedAt = DateTime.UtcNow
                });
            }

            var featuresExist = await masterDb.TenantFeatures
                .AnyAsync(f => f.TenantId == "tenant001");

            if (!featuresExist)
            {
                masterDb.TenantFeatures.AddRange(
                    new MasterDb.Entities.TenantFeature { TenantId = "tenant001", FeatureKey = "social:feed", IsEnabled = true },
                    new MasterDb.Entities.TenantFeature { TenantId = "tenant001", FeatureKey = "social:chat", IsEnabled = true },
                    new MasterDb.Entities.TenantFeature { TenantId = "tenant001", FeatureKey = "social:notifications", IsEnabled = true }
                );
            }

            await masterDb.SaveChangesAsync();
            return Ok("Tenant fixato!");
        }
    }
}

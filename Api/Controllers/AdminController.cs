using Domain.Interfaces;
using Infrastructure.Persistence;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly MasterDbContext _masterDb;
        private readonly ITenantDbContextFactory _tenantFactory;
        private readonly ITenantEncryption _encryption;

        public AdminController(
            MasterDbContext masterDb,
            ITenantDbContextFactory tenantFactory,
            ITenantEncryption encryption)
        {
            _masterDb = masterDb;
            _tenantFactory = tenantFactory;
            _encryption = encryption;
        }

        // ── Stats globali ──────────────────────────────────────

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var totalTenants = await _masterDb.Tenants.CountAsync();
            var activeTenants = await _masterDb.Tenants.CountAsync(t => t.IsActive);
            var enabledFeatures = await _masterDb.TenantFeatures.CountAsync(f => f.IsEnabled);
            var totalMigrations = await _masterDb.TenantMigrationLogs.CountAsync();
            var failedMigrations = await _masterDb.TenantMigrationLogs
                .CountAsync(m => m.Status == MigrationStatus.Failed);

            return Ok(new
            {
                TotalTenants = totalTenants,
                ActiveTenants = activeTenants,
                InactiveTenants = totalTenants - activeTenants,
                EnabledFeatures = enabledFeatures,
                TotalMigrations = totalMigrations,
                FailedMigrations = failedMigrations
            });
        }

        // ── Tenant CRUD ────────────────────────────────────────

        [HttpGet("tenants")]
        public async Task<IActionResult> GetTenants()
        {
            var tenants = await _masterDb.Tenants
                .Include(t => t.Connections)
                .Include(t => t.Features)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.TenantId,
                    t.Subdomain,
                    t.Name,
                    t.IsActive,
                    t.CreatedAt,
                    ConnectionsCount = t.Connections.Count,
                    Features = t.Features.Select(f => new
                    {
                        f.FeatureKey,
                        f.IsEnabled,
                        f.Config
                    })
                })
                .ToListAsync();

            return Ok(tenants);
        }

        [HttpGet("tenants/{tenantId}")]
        public async Task<IActionResult> GetTenant(string tenantId)
        {
            var tenant = await _masterDb.Tenants
                .Include(t => t.Connections)
                .Include(t => t.Features)
                .Include(t => t.MigrationLogs)
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null) return NotFound();

            return Ok(new
            {
                tenant.Id,
                tenant.TenantId,
                tenant.Subdomain,
                tenant.Name,
                tenant.IsActive,
                tenant.CreatedAt,
                Connections = tenant.Connections.Select(c => new
                {
                    c.Id,
                    c.DbVersion,
                    c.Region,
                    c.UpdatedAt
                }),
                Features = tenant.Features.Select(f => new
                {
                    f.Id,
                    f.FeatureKey,
                    f.IsEnabled,
                    f.Config
                }),
                MigrationLogs = tenant.MigrationLogs
                    .OrderByDescending(m => m.AppliedAt)
                    .Take(10)
                    .Select(m => new
                    {
                        m.MigrationId,
                        m.AppliedAt,
                        Status = m.Status.ToString(),
                        m.ErrorMessage
                    })
            });
        }

        [HttpPost("tenants")]
        public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
        {
            if (await _masterDb.Tenants.AnyAsync(t => t.TenantId == request.TenantId))
                return Conflict($"TenantId '{request.TenantId}' già esistente.");

            if (await _masterDb.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain))
                return Conflict($"Subdomain '{request.Subdomain}' già in uso.");

            using var transaction = await _masterDb.Database.BeginTransactionAsync();
            try
            {
                var tenant = new Tenant
                {
                    TenantId = request.TenantId,
                    Subdomain = request.Subdomain,  // valore libero
                    Name = request.Name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _masterDb.Tenants.Add(tenant);
                await _masterDb.SaveChangesAsync();

                _ = _masterDb.TenantConnections.Add(new TenantConnection
                {
                    TenantId = tenant.TenantId,
                    ConnectionStringEncrypted = _encryption.Encrypt(request.ConnectionString),
                    DbVersion = "1.0.0",
                    Region = request.Region ?? "eu-west",
                    UpdatedAt = DateTime.UtcNow
                });

                foreach (var feature in new[] { "social:feed", "social:chat", "social:notifications" })
                {
                    _masterDb.TenantFeatures.Add(new TenantFeature
                    {
                        TenantId = tenant.TenantId,
                        FeatureKey = feature,
                        IsEnabled = true
                    });
                }

                await _masterDb.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { tenant.TenantId, tenant.Subdomain, tenant.Name });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        public record CreateTenantRequest(
            string TenantId,
            string Subdomain,
            string Name,
            string ConnectionString,
            string? Region);

        [HttpPut("tenants/{tenantId}")]
        public async Task<IActionResult> UpdateTenant(string tenantId, [FromBody] UpdateTenantRequest request)
        {
            var tenant = await _masterDb.Tenants
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null) return NotFound();

            // Verifica che il nuovo subdomain non sia già in uso da un altro tenant
            if (!string.IsNullOrEmpty(request.Subdomain) &&
                request.Subdomain != tenant.Subdomain)
            {
                var subdomainInUse = await _masterDb.Tenants
                    .AnyAsync(t => t.Subdomain == request.Subdomain && t.TenantId != tenantId);

                if (subdomainInUse)
                    return Conflict($"Subdomain '{request.Subdomain}' già in uso.");

                tenant.Subdomain = request.Subdomain;

                // Invalida la cache per il vecchio subdomain
                // (il resolver lo ricaricherà automaticamente al prossimo accesso)
            }

            tenant.Name = request.Name;
            tenant.IsActive = request.IsActive;

            await _masterDb.SaveChangesAsync();
            return Ok(new { tenant.TenantId, tenant.Subdomain, tenant.Name, tenant.IsActive });
        }

        public record UpdateTenantRequest(string Name, bool IsActive, string? Subdomain);

        // ── Feature management ─────────────────────────────────

        [HttpPut("tenants/{tenantId}/features/{featureKey}")]
        public async Task<IActionResult> ToggleFeature(
            string tenantId, string featureKey,
            [FromBody] ToggleFeatureRequest request)
        {
            var feature = await _masterDb.TenantFeatures
                .FirstOrDefaultAsync(f =>
                    f.TenantId == tenantId && f.FeatureKey == featureKey);

            if (feature is null)
            {
                _masterDb.TenantFeatures.Add(new TenantFeature
                {
                    TenantId = tenantId,
                    FeatureKey = featureKey,
                    IsEnabled = request.IsEnabled,
                    Config = request.Config
                });
            }
            else
            {
                feature.IsEnabled = request.IsEnabled;
                feature.Config = request.Config ?? feature.Config;
            }

            await _masterDb.SaveChangesAsync();
            return Ok(new { tenantId, featureKey, request.IsEnabled });
        }

        // ── Utenti cross-tenant ────────────────────────────────

        [HttpGet("tenants/{tenantId}/users")]
        public async Task<IActionResult> GetTenantUsers(string tenantId)
        {
            var tenant = await _masterDb.Tenants
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null) return NotFound();

            await using var db = (BaseAppDbContext)_tenantFactory.Create();

            var users = await db.Users
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.FirstName,
                    u.LastName,
                    u.IsActive,
                    u.IsDeleted,
                    u.UserType,
                    u.LastLoginAt,
                    u.CreatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPut("tenants/{tenantId}/users/{userId}/toggle")]
        public async Task<IActionResult> ToggleUser(string tenantId, int userId)
        {
            await using var db = (BaseAppDbContext)_tenantFactory.Create();

            var user = await db.Users.FindAsync(userId);
            if (user is null) return NotFound();

            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync();

            return Ok(new { userId, user.IsActive });
        }

        [HttpGet("tenants/{tenantId}/migrations")]
        public async Task<IActionResult> GetMigrationLogs(string tenantId)
        {
            var logs = await _masterDb.TenantMigrationLogs
                .Where(m => m.TenantId == tenantId)
                .OrderByDescending(m => m.AppliedAt)
                .Select(m => new
                {
                    m.Id,
                    m.MigrationId,
                    m.AppliedAt,
                    Status = m.Status.ToString(),
                    m.ErrorMessage
                })
                .ToListAsync();

            return Ok(logs);
        }
    }

    // Request records
    public record CreateTenantRequest(
        string TenantId, string Subdomain,
        string Name, string ConnectionString, string? Region);

    public record UpdateTenantRequest(string Name, bool IsActive);
    public record ToggleFeatureRequest(bool IsEnabled, string? Config);
}

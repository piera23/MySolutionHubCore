using Application.Interfaces;
using Domain.Interfaces;
using Infrastructure.Persistence;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Api.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly MasterDbContext _masterDb;
        private readonly ITenantEncryption _encryption;
        private readonly ITenantProvisioningService _provisioning;
        private readonly IMemoryCache _cache;
        private readonly IHostEnvironment _env;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            MasterDbContext masterDb,
            ITenantEncryption encryption,
            ITenantProvisioningService provisioning,
            IMemoryCache cache,
            IHostEnvironment env,
            ILogger<AdminController> logger)
        {
            _masterDb = masterDb;
            _encryption = encryption;
            _provisioning = provisioning;
            _cache = cache;
            _env = env;
            _logger = logger;
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

            // Salva il tenant come inattivo finché il provisioning non è completato.
            // In questo modo il resolver non lo espone prima che il DB sia pronto.
            using var transaction = await _masterDb.Database.BeginTransactionAsync();
            try
            {
                var tenant = new Tenant
                {
                    TenantId = request.TenantId,
                    Subdomain = request.Subdomain,
                    Name = request.Name,
                    IsActive = false,   // attivato dopo il provisioning
                    CreatedAt = DateTime.UtcNow
                };
                _masterDb.Tenants.Add(tenant);
                await _masterDb.SaveChangesAsync();

                _masterDb.TenantConnections.Add(new TenantConnection
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
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Errore durante la creazione del tenant {TenantId}.", request.TenantId);
                return StatusCode(500, "Errore interno durante la creazione del tenant.");
            }

            // Provisioning DB: crea il DB fisico, applica le migrazioni, seed ruoli.
            // Eseguito DOPO il commit così TenantMigrationLog può usare la FK su Tenant.
            var result = await _provisioning.ProvisionAsync(request.TenantId, request.ConnectionString);

            if (!result.Success)
            {
                _logger.LogError(
                    "Provisioning fallito per {TenantId}: {Error}. Il tenant è stato creato ma è inattivo.",
                    request.TenantId, result.Error);

                return StatusCode(500, new
                {
                    message = "Il tenant è stato registrato ma il provisioning del DB è fallito. " +
                              "Usa POST /api/admin/tenants/{tenantId}/provision per riprovare.",
                    tenantId = request.TenantId,
                    error = result.Error
                });
            }

            // Attiva il tenant ora che il DB è pronto
            var tenantToActivate = await _masterDb.Tenants
                .FirstAsync(t => t.TenantId == request.TenantId);
            tenantToActivate.IsActive = true;
            await _masterDb.SaveChangesAsync();

            return Ok(new
            {
                tenantToActivate.TenantId,
                tenantToActivate.Subdomain,
                tenantToActivate.Name,
                tenantToActivate.IsActive
            });
        }

        [HttpPost("tenants/{tenantId}/provision")]
        public async Task<IActionResult> ProvisionTenant(string tenantId)
        {
            var connection = await _masterDb.TenantConnections
                .FirstOrDefaultAsync(c => c.TenantId == tenantId);

            if (connection is null)
                return NotFound($"Nessuna connection string trovata per il tenant '{tenantId}'.");

            var plainCs = _encryption.Decrypt(connection.ConnectionStringEncrypted);
            var result = await _provisioning.ProvisionAsync(tenantId, plainCs);

            if (!result.Success)
                return StatusCode(500, new { message = "Provisioning fallito.", error = result.Error });

            // Attiva il tenant se era inattivo per via di un provisioning precedente fallito
            var tenant = await _masterDb.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant is not null && !tenant.IsActive)
            {
                tenant.IsActive = true;
                await _masterDb.SaveChangesAsync();
            }

            return Ok(new { message = "Provisioning completato.", tenantId });
        }

        [HttpPut("tenants/{tenantId}")]
        public async Task<IActionResult> UpdateTenant(string tenantId, [FromBody] UpdateTenantRequest request)
        {
            var tenant = await _masterDb.Tenants
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null) return NotFound();

            var oldSubdomain = tenant.Subdomain;

            if (!string.IsNullOrEmpty(request.Subdomain) &&
                request.Subdomain != tenant.Subdomain)
            {
                var subdomainInUse = await _masterDb.Tenants
                    .AnyAsync(t => t.Subdomain == request.Subdomain && t.TenantId != tenantId);

                if (subdomainInUse)
                    return Conflict($"Subdomain '{request.Subdomain}' già in uso.");

                tenant.Subdomain = request.Subdomain;
            }

            tenant.Name = request.Name;
            tenant.IsActive = request.IsActive;

            await _masterDb.SaveChangesAsync();

            // Invalida la cache per il vecchio subdomain (e il nuovo se è cambiato)
            _cache.Remove($"tenant:{oldSubdomain}");
            if (tenant.Subdomain != oldSubdomain)
                _cache.Remove($"tenant:{tenant.Subdomain}");

            return Ok(new { tenant.TenantId, tenant.Subdomain, tenant.Name, tenant.IsActive });
        }

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

            // Invalida la cache del tenant così la feature è immediatamente visibile
            var subdomain = await _masterDb.Tenants
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Subdomain)
                .FirstOrDefaultAsync();

            if (subdomain is not null)
                _cache.Remove($"tenant:{subdomain}");

            return Ok(new { tenantId, featureKey, request.IsEnabled });
        }

        // ── Utenti cross-tenant ────────────────────────────────

        [HttpGet("tenants/{tenantId}/users")]
        public async Task<IActionResult> GetTenantUsers(string tenantId)
        {
            await using var db = await OpenTenantDbAsync(tenantId);
            if (db is null) return NotFound($"Tenant '{tenantId}' non trovato.");

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
            await using var db = await OpenTenantDbAsync(tenantId);
            if (db is null) return NotFound($"Tenant '{tenantId}' non trovato.");

            var user = await db.Users.FindAsync(userId);
            if (user is null) return NotFound();

            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync();

            return Ok(new { userId, user.IsActive });
        }

        // ── Tenant Settings ────────────────────────────────────

        [HttpGet("tenants/{tenantId}/settings")]
        public async Task<IActionResult> GetSettings(string tenantId)
        {
            var settings = await _masterDb.TenantSettings
                .Where(s => s.TenantId == tenantId)
                .OrderBy(s => s.Key)
                .Select(s => new { s.Id, s.Key, s.Value, s.UpdatedAt })
                .ToListAsync();

            return Ok(settings);
        }

        [HttpPut("tenants/{tenantId}/settings/{key}")]
        public async Task<IActionResult> UpsertSetting(
            string tenantId, string key,
            [FromBody] UpsertSettingRequest request)
        {
            var setting = await _masterDb.TenantSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key);

            if (setting is null)
            {
                // Verifica che il tenant esista prima di inserire
                if (!await _masterDb.Tenants.AnyAsync(t => t.TenantId == tenantId))
                    return NotFound($"Tenant '{tenantId}' non trovato.");

                _masterDb.TenantSettings.Add(new MasterDb.Entities.TenantSetting
                {
                    TenantId = tenantId,
                    Key = key,
                    Value = request.Value,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Value = request.Value;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _masterDb.SaveChangesAsync();

            // Invalida la cache così le nuove impostazioni sono immediatamente visibili
            var subdomain = await _masterDb.Tenants
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Subdomain)
                .FirstOrDefaultAsync();
            if (subdomain is not null)
                _cache.Remove($"tenant:{subdomain}");

            return Ok(new { tenantId, key, request.Value });
        }

        [HttpDelete("tenants/{tenantId}/settings/{key}")]
        public async Task<IActionResult> DeleteSetting(string tenantId, string key)
        {
            var setting = await _masterDb.TenantSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key);

            if (setting is null) return NotFound();

            _masterDb.TenantSettings.Remove(setting);
            await _masterDb.SaveChangesAsync();

            var subdomain = await _masterDb.Tenants
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.Subdomain)
                .FirstOrDefaultAsync();
            if (subdomain is not null)
                _cache.Remove($"tenant:{subdomain}");

            return NoContent();
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

        // ── Helper: apre il DB di uno specifico tenant dal route param ─

        /// <summary>
        /// Crea un BaseAppDbContext direttamente dalla connection string del tenant
        /// indicato nel route, indipendentemente dall'header X-Tenant-Id corrente.
        /// Restituisce null se il tenant o la sua connessione non esistono.
        /// </summary>
        private async Task<BaseAppDbContext?> OpenTenantDbAsync(string tenantId)
        {
            var connection = await _masterDb.TenantConnections
                .FirstOrDefaultAsync(c => c.TenantId == tenantId);

            if (connection is null) return null;

            var plainCs = _encryption.Decrypt(connection.ConnectionStringEncrypted);

            var options = new DbContextOptionsBuilder<BaseAppDbContext>();
            if (_env.IsDevelopment())
                options.UseSqlite(plainCs);
            else
                options.UseSqlServer(plainCs);

            return new BaseAppDbContext(options.Options);
        }
    }

    // Request records
    public record CreateTenantRequest(
        [Required, MinLength(3), MaxLength(50), RegularExpression(@"^[a-z0-9\-]+$",
            ErrorMessage = "TenantId può contenere solo lettere minuscole, numeri e trattini.")]
        string TenantId,

        [Required, MinLength(3), MaxLength(63), RegularExpression(@"^[a-z0-9\-]+$",
            ErrorMessage = "Subdomain può contenere solo lettere minuscole, numeri e trattini.")]
        string Subdomain,

        [Required, MaxLength(200)] string Name,
        [Required, MinLength(10)] string ConnectionString,
        [MaxLength(50)] string? Region);

    public record UpdateTenantRequest(
        [Required, MaxLength(200)] string Name,
        bool IsActive,
        [MinLength(3), MaxLength(63)] string? Subdomain);

    public record ToggleFeatureRequest(bool IsEnabled, [MaxLength(2000)] string? Config);

    public record UpsertSettingRequest([Required, MaxLength(2000)] string Value);
}

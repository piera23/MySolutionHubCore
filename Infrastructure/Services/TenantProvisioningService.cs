using Application.Interfaces;
using Domain.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class TenantProvisioningService : ITenantProvisioningService
    {
        private readonly MasterDbContext _masterDb;
        private readonly IEmailService _email;
        private readonly ILogger<TenantProvisioningService> _logger;

        public TenantProvisioningService(
            MasterDbContext masterDb,
            IEmailService email,
            ILogger<TenantProvisioningService> logger)
        {
            _masterDb = masterDb;
            _email = email;
            _logger = logger;
        }

        public async Task<ProvisioningResult> ProvisionAsync(
            string tenantId,
            string connectionString,
            string? adminEmail = null,
            int trialDays = 0,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Avvio provisioning per tenant {TenantId}.", tenantId);

            // Registra il tentativo nel migration log
            var log = new TenantMigrationLog
            {
                TenantId = tenantId,
                MigrationId = "InitialProvisioning",
                AppliedAt = DateTime.UtcNow,
                Status = MigrationStatus.Pending
            };
            _masterDb.TenantMigrationLogs.Add(log);
            await _masterDb.SaveChangesAsync(ct);

            try
            {
                var options = BuildOptions(connectionString);
                await using var db = new Persistence.BaseAppDbContext(options);

                // Crea il DB e applica tutte le migrazioni pendenti
                await db.Database.MigrateAsync(ct);
                _logger.LogInformation("Migrazioni applicate per tenant {TenantId}.", tenantId);

                // Seed ruoli base
                await SeedRolesAsync(db, ct);
                _logger.LogInformation("Ruoli base creati per tenant {TenantId}.", tenantId);

                // Aggiorna il log con esito positivo
                log.Status = MigrationStatus.Completed;

                // Periodo di trial: salva la data di scadenza in TenantSettings
                if (trialDays > 0)
                {
                    var trialEndsAt = DateTime.UtcNow.AddDays(trialDays);
                    _masterDb.TenantSettings.Add(new TenantSetting
                    {
                        TenantId = tenantId,
                        Key = "Trial:EndsAt",
                        Value = trialEndsAt.ToString("O"),
                        UpdatedAt = DateTime.UtcNow
                    });
                    _logger.LogInformation(
                        "Trial period impostato per tenant {TenantId}: scadenza {TrialEndsAt}.",
                        tenantId, trialEndsAt);
                }

                await _masterDb.SaveChangesAsync(ct);

                // Email di benvenuto all'admin del tenant
                if (!string.IsNullOrWhiteSpace(adminEmail))
                    await SendWelcomeEmailAsync(tenantId, adminEmail, trialDays, ct);

                _logger.LogInformation("Provisioning completato per tenant {TenantId}.", tenantId);
                return new ProvisioningResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provisioning fallito per tenant {TenantId}.", tenantId);
                log.Status = MigrationStatus.Failed;
                log.ErrorMessage = ex.Message;
                await _masterDb.SaveChangesAsync(ct);

                return new ProvisioningResult(false, ex.Message);
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        private async Task SendWelcomeEmailAsync(
            string tenantId, string adminEmail, int trialDays, CancellationToken ct)
        {
            try
            {
                var trialInfo = trialDays > 0
                    ? $"<p>Il tuo piano di prova è attivo per <strong>{trialDays} giorni</strong>.</p>"
                    : string.Empty;

                var body = $"""
                    <h2>Benvenuto su MySolutionHub!</h2>
                    <p>Il tuo tenant <strong>{tenantId}</strong> è stato configurato correttamente.</p>
                    {trialInfo}
                    <p>Puoi accedere subito alla piattaforma e iniziare a configurare il tuo spazio.</p>
                    <p>Per assistenza, contatta il nostro supporto.</p>
                    """;

                await _email.SendAsync(adminEmail, $"Benvenuto su MySolutionHub — {tenantId}", body, ct);
                _logger.LogInformation("Email di benvenuto inviata a {AdminEmail} per tenant {TenantId}.",
                    adminEmail, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore invio email di benvenuto per tenant {TenantId}.", tenantId);
            }
        }

        private static DbContextOptions BuildOptions(string connectionString)
        {
            var builder = new DbContextOptionsBuilder();
            builder.UseNpgsql(connectionString);
            return builder.Options;
        }

        private static async Task SeedRolesAsync(
            Persistence.BaseAppDbContext db,
            CancellationToken ct)
        {
            var roleStore = new RoleStore<IdentityRole<int>, Persistence.BaseAppDbContext, int>(db);
            var roleManager = new RoleManager<IdentityRole<int>>(
                roleStore,
                Array.Empty<IRoleValidator<IdentityRole<int>>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!);

            foreach (var role in new[] { "Internal", "External", "Admin" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
            }
        }
    }
}

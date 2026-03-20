namespace Application.Interfaces
{
    /// <summary>
    /// Gestisce il provisioning fisico del database di un tenant:
    /// crea il DB, applica le migrazioni EF e fa il seed dei ruoli base.
    /// </summary>
    public interface ITenantProvisioningService
    {
        /// <summary>
        /// Crea il database del tenant, applica tutte le migrazioni pendenti
        /// e popola i ruoli base (Internal, External, Admin).
        /// Scrive il risultato in TenantMigrationLog.
        /// </summary>
        Task<ProvisioningResult> ProvisionAsync(
            string tenantId,
            string connectionString,
            CancellationToken ct = default);
    }

    public record ProvisioningResult(bool Success, string? Error = null);
}

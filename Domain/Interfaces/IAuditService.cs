namespace Domain.Interfaces
{
    public interface IAuditService
    {
        Task LogAsync(
            string action,
            string? tenantId = null,
            string? entityType = null,
            string? entityId = null,
            string? changes = null,
            CancellationToken ct = default);
    }
}

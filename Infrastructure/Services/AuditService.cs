using Domain.Interfaces;
using MasterDb.Entities;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Infrastructure.Services
{
    public class AuditService : IAuditService
    {
        private readonly MasterDbContext _masterDb;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(MasterDbContext masterDb, IHttpContextAccessor httpContextAccessor)
        {
            _masterDb = masterDb;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(
            string action,
            string? tenantId = null,
            string? entityType = null,
            string? entityId = null,
            string? changes = null,
            CancellationToken ct = default)
        {
            var ctx = _httpContextAccessor.HttpContext;
            var actorId = ctx?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "system";
            var actorName = ctx?.User.FindFirst(ClaimTypes.Name)?.Value
                ?? ctx?.User.FindFirst("name")?.Value
                ?? "system";
            var ip = ctx?.Connection.RemoteIpAddress?.ToString();

            _masterDb.AuditLogs.Add(new AuditLog
            {
                Action = action,
                TenantId = tenantId,
                ActorId = actorId,
                ActorName = actorName,
                EntityType = entityType,
                EntityId = entityId,
                Changes = changes,
                IpAddress = ip,
                Timestamp = DateTime.UtcNow
            });

            await _masterDb.SaveChangesAsync(ct);
        }
    }
}

using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;
using MasterDb.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Background service che elabora i messaggi outbox di ogni tenant attivo.
    /// Garantisce la consegna at-least-once di eventi di dominio (fan-out attività,
    /// notifiche, email) anche in caso di crash del processo primario.
    /// Scheduling: avvio dopo 20s + polling ogni 30s.
    /// </summary>
    public class OutboxProcessorService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay   = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
        private const int BatchSize   = 50;
        private const int MaxRetries  = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxProcessorService> _logger;

        public OutboxProcessorService(
            IServiceScopeFactory scopeFactory,
            ILogger<OutboxProcessorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>Punto d'ingresso per trigger manuali da Hangfire.</summary>
        public async Task TriggerManualRunAsync(CancellationToken ct)
        {
            _logger.LogInformation("OutboxProcessor avviato manualmente.");
            await ProcessAllTenantsAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessAllTenantsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore durante il ciclo di elaborazione outbox.");
                }

                await Task.Delay(PollingInterval, stoppingToken);
            }
        }

        // ── Per-tenant processing ──────────────────────────────────────────────

        private async Task ProcessAllTenantsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var masterDb   = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            var encryption = scope.ServiceProvider.GetRequiredService<ITenantEncryption>();

            var tenants = await masterDb.Tenants
                .Where(t => t.IsActive)
                .Include(t => t.Connections)
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var tenant in tenants)
            {
                if (ct.IsCancellationRequested) break;

                var conn = tenant.Connections.FirstOrDefault();
                if (conn is null) continue;

                try
                {
                    var plainCs = encryption.Decrypt(conn.ConnectionStringEncrypted);
                    await ProcessTenantOutboxAsync(tenant.TenantId, plainCs, scope.ServiceProvider, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore elaborazione outbox tenant {TenantId}.", tenant.TenantId);
                }
            }
        }

        private async Task ProcessTenantOutboxAsync(
            string tenantId,
            string connectionString,
            IServiceProvider services,
            CancellationToken ct)
        {
            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            await using var db = new BaseAppDbContext(options);

            var pending = await db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
                .OrderBy(m => m.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (!pending.Any()) return;

            _logger.LogInformation(
                "Outbox: {Count} messaggi da elaborare per tenant {TenantId}.",
                pending.Count, tenantId);

            foreach (var msg in pending)
            {
                if (ct.IsCancellationRequested) break;
                await DispatchMessageAsync(msg, tenantId, connectionString, services, ct);
            }

            await db.SaveChangesAsync(ct);
        }

        // ── Dispatch per tipo evento ───────────────────────────────────────────

        private async Task DispatchMessageAsync(
            OutboxMessage msg,
            string tenantId,
            string connectionString,
            IServiceProvider services,
            CancellationToken ct)
        {
            try
            {
                switch (msg.EventType)
                {
                    case "activity.fanout":
                        await HandleActivityFanoutAsync(msg.Payload, tenantId, connectionString, services, ct);
                        break;

                    case "notification.push":
                        await HandleNotificationPushAsync(msg.Payload, tenantId, services, ct);
                        break;

                    default:
                        _logger.LogWarning(
                            "Outbox: tipo evento sconosciuto '{EventType}' (id={Id}).",
                            msg.EventType, msg.Id);
                        break;
                }

                msg.ProcessedAt = DateTime.UtcNow;
                _logger.LogDebug("Outbox: messaggio {Id} elaborato.", msg.Id);
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Outbox: errore elaborazione messaggio {Id} (tentativo {Retry}/{Max}).",
                    msg.Id, msg.RetryCount, MaxRetries);
            }
        }

        // ── Handler: activity.fanout ───────────────────────────────────────────

        /// <summary>
        /// Invia notifiche a tutti i follower dell'autore dell'attività.
        /// Payload: { "activityId": int, "authorId": int, "eventType": string }
        /// </summary>
        private async Task HandleActivityFanoutAsync(
            string payloadJson,
            string tenantId,
            string connectionString,
            IServiceProvider services,
            CancellationToken ct)
        {
            using var doc    = JsonDocument.Parse(payloadJson);
            var activityId   = doc.RootElement.GetProperty("activityId").GetInt32();
            var authorId     = doc.RootElement.GetProperty("authorId").GetInt32();
            var eventType    = doc.RootElement.GetProperty("eventType").GetString() ?? "activity";

            var options = new DbContextOptionsBuilder<BaseAppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            await using var db = new BaseAppDbContext(options);

            // Trova tutti i follower attivi dell'autore
            var followerIds = await db.UserFollows
                .Where(f => f.FollowedId == authorId && f.IsActive)
                .Select(f => f.FollowerId)
                .ToListAsync(ct);

            if (!followerIds.Any()) return;

            // Recupera le info dell'autore
            var author = await db.Users.FindAsync(new object[] { authorId }, ct);
            var authorName = author?.UserName ?? $"user_{authorId}";

            // Spedisci notifiche via SignalR a ciascun follower
            var notificationHub = services
                .GetRequiredService<IHubContext<Hubs.NotificationHub, INotificationHub>>();

            foreach (var followerId in followerIds)
            {
                var groupName = $"{tenantId}:user:{followerId}";
                await notificationHub.Clients.Group(groupName).ReceiveNotification(
                    new NotificationDto(
                        Id:         0,
                        Type:       "activity.new",
                        Title:      $"Nuova attività da {authorName}",
                        Message:    $"{authorName} ha pubblicato un nuovo {eventType}.",
                        EntityId:   activityId.ToString(),
                        EntityType: "ActivityEvent",
                        IsRead:     false,
                        CreatedAt:  DateTime.UtcNow));
            }

            _logger.LogInformation(
                "Outbox fanout: {Count} notifiche inviate per attività {ActivityId} (tenant {TenantId}).",
                followerIds.Count, activityId, tenantId);
        }

        // ── Handler: notification.push ─────────────────────────────────────────

        /// <summary>
        /// Invia una singola notifica push via SignalR.
        /// Payload: { "tenantId": string, "userId": int, "type": string,
        ///            "title": string, "message": string }
        /// </summary>
        private async Task HandleNotificationPushAsync(
            string payloadJson,
            string tenantId,
            IServiceProvider services,
            CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var userId    = doc.RootElement.GetProperty("userId").GetInt32();
            var type      = doc.RootElement.GetProperty("type").GetString() ?? "info";
            var title     = doc.RootElement.GetProperty("title").GetString() ?? "";
            var message   = doc.RootElement.GetProperty("message").GetString() ?? "";

            var hub = services
                .GetRequiredService<IHubContext<Hubs.NotificationHub, INotificationHub>>();

            var groupName = $"{tenantId}:user:{userId}";
            await hub.Clients.Group(groupName).ReceiveNotification(
                new NotificationDto(
                    Id:         0,
                    Type:       type,
                    Title:      title,
                    Message:    message,
                    EntityId:   null,
                    EntityType: null,
                    IsRead:     false,
                    CreatedAt:  DateTime.UtcNow));
        }
    }
}

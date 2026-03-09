using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ITenantDbContextFactory _dbFactory;
        private readonly IHubContext<Infrastructure.Hubs.NotificationHub, INotificationHub> _hub;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            ITenantDbContextFactory dbFactory,
            IHubContext<Infrastructure.Hubs.NotificationHub, INotificationHub> hub,
            ITenantContext tenantContext,
            ILogger<NotificationService> logger)
        {
            _dbFactory = dbFactory;
            _hub = hub;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async Task SendAsync(
            int userId,
            string type,
            string title,
            string message,
            string? entityId = null,
            string? entityType = null,
            int? senderId = null,
            CancellationToken ct = default)
        {
            await using var db = (Infrastructure.Persistence.BaseAppDbContext)_dbFactory.Create();

            // 1. Persisti la notifica nel DB
            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                EntityId = entityId,
                EntityType = entityType,
                SenderId = senderId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);

            // 2. Invia in real-time via SignalR se l'utente è connesso
            var groupName = $"{_tenantContext.TenantId}:user:{userId}";
            await _hub.Clients.Group(groupName).ReceiveNotification(new NotificationDto(
                Id: notification.Id,
                Type: type,
                Title: title,
                Message: message,
                EntityId: entityId,
                EntityType: entityType,
                IsRead: false,
                CreatedAt: notification.CreatedAt
            ));

            _logger.LogInformation(
                "Notifica inviata — Tenant: {TenantId}, User: {UserId}, Type: {Type}",
                _tenantContext.TenantId, userId, type);
        }

        public async Task SendToAllAsync(
            string type,
            string title,
            string message,
            CancellationToken ct = default)
        {
            var groupName = $"{_tenantContext.TenantId}:all";
            await _hub.Clients.Group(groupName).ReceiveNotification(new NotificationDto(
                Id: 0,
                Type: type,
                Title: title,
                Message: message,
                EntityId: null,
                EntityType: null,
                IsRead: false,
                CreatedAt: DateTime.UtcNow
            ));
        }

        public async Task<IEnumerable<NotificationDto>> GetPendingAsync(
            int userId,
            CancellationToken ct = default)
        {
            await using var db = (Infrastructure.Persistence.BaseAppDbContext)_dbFactory.Create();

            return await db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new NotificationDto(
                    n.Id, n.Type, n.Title, n.Message,
                    n.EntityId, n.EntityType, n.IsRead, n.CreatedAt))
                .ToListAsync(ct);
        }

        public async Task MarkAsReadAsync(
            int notificationId,
            int userId,
            CancellationToken ct = default)
        {
            await using var db = (Infrastructure.Persistence.BaseAppDbContext)_dbFactory.Create();

            var notification = await db.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

            if (notification is null) return;

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        public async Task MarkAllAsReadAsync(
            int userId,
            CancellationToken ct = default)
        {
            await using var db = (Infrastructure.Persistence.BaseAppDbContext)_dbFactory.Create();

            await db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        }
    }
}

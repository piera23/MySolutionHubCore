using Application.Interfaces;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Infrastructure.Hubs
{
    [Authorize]
    public class NotificationHub : Hub<INotificationHub>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(
            ITenantContext tenantContext,
            ILogger<NotificationHub> logger)
        {
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            var tenantId = _tenantContext.TenantId;

            // Gruppo tenant:user — isola il traffico per tenant
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:user:{userId}");

            // Gruppo tenant — per broadcast a tutti gli utenti del tenant
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:all");

            _logger.LogInformation(
                "NotificationHub connected — Tenant: {TenantId}, User: {UserId}",
                tenantId, userId);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserId();
            var tenantId = _tenantContext.TenantId;

            _logger.LogInformation(
                "NotificationHub disconnected — Tenant: {TenantId}, User: {UserId}",
                tenantId, userId);

            await base.OnDisconnectedAsync(exception);
        }

        // Client chiama questo metodo per marcare una notifica come letta
        public async Task MarkAsRead(int notificationId)
        {
            var userId = GetUserId();
            _logger.LogInformation(
                "Notifica {NotificationId} marcata come letta da User {UserId}",
                notificationId, userId);

            // In futuro qui chiameremo il servizio notifiche
            await Clients.Caller.NotificationRead(notificationId);
        }

        private int GetUserId()
        {
            var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}

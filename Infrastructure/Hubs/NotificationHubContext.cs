using Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Hubs
{
    public class NotificationHubContext : INotificationHubContext
    {
        private readonly IHubContext<NotificationHub, INotificationHub> _hub;

        public NotificationHubContext(IHubContext<NotificationHub, INotificationHub> hub)
        {
            _hub = hub;
        }

        public async Task SendNotificationAsync(int userId, string tenantId, NotificationDto notification)
        {
            await _hub.Clients
                .Group($"{tenantId}:user:{userId}")
                .ReceiveNotification(notification);
        }

        public async Task SendNotificationToAllAsync(string tenantId, NotificationDto notification)
        {
            await _hub.Clients
                .Group($"{tenantId}:all")
                .ReceiveNotification(notification);
        }
    }
}

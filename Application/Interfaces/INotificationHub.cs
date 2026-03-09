using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface INotificationHub
    {
        Task ReceiveNotification(NotificationDto notification);
        Task PendingNotifications(IEnumerable<NotificationDto> notifications);
        Task NotificationRead(int notificationId);
    }

    public record NotificationDto(
        int Id,
        string Type,
        string Title,
        string Message,
        string? EntityId,
        string? EntityType,
        bool IsRead,
        DateTime CreatedAt
    );
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface INotificationService
    {
        Task SendAsync(int userId, string type, string title, string message,
            string? entityId = null, string? entityType = null,
            int? senderId = null, CancellationToken ct = default);

        Task SendToAllAsync(string type, string title, string message,
            CancellationToken ct = default);

        Task<IEnumerable<NotificationDto>> GetPendingAsync(int userId,
            CancellationToken ct = default);

        Task MarkAsReadAsync(int notificationId, int userId,
            CancellationToken ct = default);

        Task MarkAllAsReadAsync(int userId, CancellationToken ct = default);
    }
}

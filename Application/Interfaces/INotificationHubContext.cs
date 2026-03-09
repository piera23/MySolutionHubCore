using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface INotificationHubContext
    {
        Task SendNotificationAsync(int userId, string tenantId, NotificationDto notification);
        Task SendNotificationToAllAsync(string tenantId, NotificationDto notification);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Interfaces
{
    public interface IActivityService
    {
        Task PublishAsync(int userId, string eventType,
            string? entityId = null, string? entityType = null,
            object? payload = null, CancellationToken ct = default);

        Task<IEnumerable<ActivityEventDto>> GetFeedAsync(int userId,
            int page = 1, int pageSize = 20, CancellationToken ct = default);

        Task FollowAsync(int followerId, int followedId, CancellationToken ct = default);
        Task UnfollowAsync(int followerId, int followedId, CancellationToken ct = default);
        Task ReactAsync(int eventId, int userId, string reactionType = "like", CancellationToken ct = default);
    }

    public record ActivityEventDto(
        int Id,
        int UserId,
        string Username,
        string? AvatarUrl,
        string EventType,
        string? EntityId,
        string? EntityType,
        string? Payload,
        int ReactionsCount,
        bool HasReacted,
        DateTime CreatedAt
    );
}

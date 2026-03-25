using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class OutboxPublisher : IOutboxPublisher
    {
        private readonly BaseAppDbContext _db;

        public OutboxPublisher(BaseAppDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc/>
        public void Enqueue(string eventType, object payload)
        {
            _db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = eventType,
                Payload   = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow
            });
            // Non chiama SaveChanges: l'unità di lavoro esterna fa il commit.
        }

        /// <inheritdoc/>
        public async Task EnqueueAndSaveAsync(
            string eventType, object payload, CancellationToken ct = default)
        {
            Enqueue(eventType, payload);
            await _db.SaveChangesAsync(ct);
        }
    }
}

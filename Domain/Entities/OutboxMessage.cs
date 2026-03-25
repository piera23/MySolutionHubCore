namespace Domain.Entities
{
    /// <summary>
    /// Messaggio nella outbox per la consegna garantita di eventi di dominio.
    /// Pattern: scrivi nella stessa transazione dell'operazione di business,
    /// poi un processor in background legge e consegna (at-least-once).
    /// </summary>
    public class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Tipo evento: es. "activity.fanout", "notification.push", "email.send".</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>Payload JSON dell'evento.</summary>
        public string Payload { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Impostato quando il processor ha consegnato con successo.</summary>
        public DateTime? ProcessedAt { get; set; }

        /// <summary>Numero di tentativi effettuati (max 5 prima di rinunciare).</summary>
        public int RetryCount { get; set; }

        /// <summary>Testo dell'ultimo errore, se presente.</summary>
        public string? LastError { get; set; }
    }
}

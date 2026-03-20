namespace MasterDb.Entities
{
    /// <summary>
    /// Traccia le operazioni amministrative sensibili (creazione tenant,
    /// modifica feature, settings, attivazione/disattivazione utenti, ecc.).
    /// </summary>
    public class AuditLog
    {
        public int Id { get; set; }

        /// <summary>Tenant coinvolto nell'operazione (null per operazioni globali).</summary>
        public string? TenantId { get; set; }

        /// <summary>ID dell'utente che ha eseguito l'azione.</summary>
        public string ActorId { get; set; } = string.Empty;

        /// <summary>Username dell'utente che ha eseguito l'azione.</summary>
        public string ActorName { get; set; } = string.Empty;

        /// <summary>Nome dell'azione (es. "CreateTenant", "ToggleFeature", "MakeAdmin").</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Tipo dell'entità modificata (es. "Tenant", "TenantFeature", "User").</summary>
        public string? EntityType { get; set; }

        /// <summary>ID dell'entità modificata.</summary>
        public string? EntityId { get; set; }

        /// <summary>Dettagli della modifica in formato testo libero o JSON.</summary>
        public string? Changes { get; set; }

        /// <summary>Indirizzo IP del richiedente.</summary>
        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

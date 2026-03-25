namespace Domain.Interfaces
{
    /// <summary>
    /// Scrive messaggi nella outbox del tenant corrente.
    /// Deve essere chiamato nella stessa transazione dell'operazione di business
    /// per garantire consistenza (nessun evento perso in caso di crash).
    /// </summary>
    public interface IOutboxPublisher
    {
        /// <summary>
        /// Accoda un evento nella outbox. L'operazione è sincrona sul DbContext
        /// (non fa SaveChanges — deve essere incluso nel SaveChanges dell'operazione
        /// che ha generato l'evento).
        /// </summary>
        void Enqueue(string eventType, object payload);

        /// <summary>
        /// Accoda un evento e persiste immediatamente (SaveChangesAsync standalone).
        /// Usare solo quando non si è in una transazione esistente.
        /// </summary>
        Task EnqueueAndSaveAsync(string eventType, object payload, CancellationToken ct = default);
    }
}

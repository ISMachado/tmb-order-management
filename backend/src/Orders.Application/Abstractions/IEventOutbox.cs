namespace Orders.Application.Abstractions;

/// <summary>
/// Transactional Outbox: o evento e gravado na mesma transacao da mudanca de estado
/// (no proximo SaveChanges) e publicado no broker depois, por um processo em background.
/// Assim nunca existe pedido salvo sem mensagem, nem mensagem sem pedido salvo.
/// </summary>
public interface IEventOutbox
{
    void Enqueue<TData>(string eventType, Guid correlationId, TData data);
}

/// <summary>
/// Inbox do consumidor: registra mensagens ja processadas para garantir idempotencia.
/// </summary>
public interface IInbox
{
    Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken);

    void MarkProcessed(Guid messageId, string consumer, DateTimeOffset processedAt);
}

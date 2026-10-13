namespace Orders.Infrastructure.Persistence;

/// <summary>Evento aguardando publicacao no broker (tabela outbox_messages).</summary>
public sealed class OutboxMessage
{
    /// <summary>Tambem e o MessageId publicado no broker (chave de idempotencia do consumidor).</summary>
    public Guid Id { get; init; }

    public required string EventType { get; init; }

    public required string RoutingKey { get; init; }

    public Guid CorrelationId { get; init; }

    /// <summary>Envelope completo serializado (jsonb).</summary>
    public required string Payload { get; init; }

    /// <summary>Contexto W3C (traceparent) de quem criou o evento, para o trace continuar no broker e no worker.</summary>
    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }
}

/// <summary>Mensagem ja processada por um consumidor (tabela inbox_messages).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Consumer { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

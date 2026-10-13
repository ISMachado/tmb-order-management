using System.Text.Json;

namespace Orders.Contracts;

/// <summary>
/// Envelope padrao de toda mensagem publicada no broker.
/// <para><c>MessageId</c>: identificador unico da mensagem (chave de idempotencia do consumidor).</para>
/// <para><c>CorrelationId</c>: sempre o Id do pedido (CorrelationId = OrderId).</para>
/// <para><c>EventType</c>: nome do evento (ex.: OrderCreated).</para>
/// </summary>
public sealed record IntegrationEvent(
    Guid MessageId,
    string EventType,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    JsonElement Data)
{
    public T GetData<T>() =>
        Data.Deserialize<T>(MessagingJson.Options)
        ?? throw new InvalidOperationException($"Payload vazio para o evento {EventType} ({MessageId}).");
}

public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderStatusChanged = "OrderStatusChanged";
}

public sealed record OrderCreatedData(
    Guid OrderId,
    string Cliente,
    string Produto,
    decimal Valor,
    DateTimeOffset DataCriacao);

public sealed record OrderStatusChangedData(
    Guid OrderId,
    string? StatusAnterior,
    string StatusNovo,
    DateTimeOffset AlteradoEm);

namespace Orders.Contracts;

/// <summary>Nomes de exchanges, filas e routing keys compartilhados entre API e Worker.</summary>
public static class MessagingTopology
{
    public const string EventsExchange = "orders.events";
    public const string DeadLetterExchange = "orders.events.dlx";

    public const string OrderCreatedRoutingKey = "order.created";
    public const string OrderStatusChangedRoutingKey = "order.status-changed";

    public const string OrderCreatedQueue = "orders.order-created.worker";
    public const string OrderCreatedDeadLetterQueue = "orders.order-created.worker.dlq";

    /// <summary>Prefixo da fila exclusiva que cada instancia da API usa para repassar mudancas de status via SignalR.</summary>
    public const string StatusChangedApiQueuePrefix = "orders.status-changed.api";

    public static string RoutingKeyFor(string eventType) => eventType switch
    {
        EventTypes.OrderCreated => OrderCreatedRoutingKey,
        EventTypes.OrderStatusChanged => OrderStatusChangedRoutingKey,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Evento sem routing key mapeada."),
    };
}

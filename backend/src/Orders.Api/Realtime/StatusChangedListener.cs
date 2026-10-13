using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Orders.Contracts;
using Orders.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace Orders.Api.Realtime;

/// <summary>
/// Escuta os eventos OrderStatusChanged (publicados pelo worker via outbox) e os repassa aos navegadores via SignalR.
/// Cada instancia da API usa uma fila exclusiva e descartavel: todas as instancias recebem todos os eventos
/// e cada uma notifica apenas os seus clientes conectados.
/// </summary>
public sealed class StatusChangedListener(
    IHubContext<OrdersHub> hub,
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<StatusChangedListener> logger)
    : RabbitMqConsumerService(connectionProvider, options, logger)
{
    protected override string ConsumerName => "status-changed-listener";

    protected override async Task<string> DeclareQueueAsync(IChannel channel, CancellationToken ct)
    {
        await RabbitMqTopology.DeclareExchangesAsync(channel, ct);

        var queue = await channel.QueueDeclareAsync(
            queue: $"{MessagingTopology.StatusChangedApiQueuePrefix}.{Environment.MachineName}.{Guid.NewGuid():N}",
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: ct);
        await channel.QueueBindAsync(queue.QueueName, MessagingTopology.EventsExchange, MessagingTopology.OrderStatusChangedRoutingKey, cancellationToken: ct);
        return queue.QueueName;
    }

    protected override async Task HandleAsync(IntegrationEvent message, CancellationToken ct)
    {
        if (message.EventType != EventTypes.OrderStatusChanged)
        {
            return;
        }

        var data = message.GetData<OrderStatusChangedData>();
        var notification = new OrderStatusChangedNotification(data.OrderId, data.StatusAnterior, data.StatusNovo, data.AlteradoEm);

        await hub.Clients.Group(OrdersHub.AllOrdersGroup).SendAsync("OrderStatusChanged", notification, ct);
    }
}

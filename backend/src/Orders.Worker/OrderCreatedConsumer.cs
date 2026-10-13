using Microsoft.Extensions.Options;
using Orders.Application.Processing;
using Orders.Contracts;
using Orders.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace Orders.Worker;

/// <summary>
/// Consome OrderCreated: Pendente -> Processando -> (5s) -> Finalizado.
/// A logica de negocio e idempotencia vive em <see cref="OrderProcessor"/>; aqui so conectamos fila e caso de uso.
/// </summary>
public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderCreatedConsumer> logger)
    : RabbitMqConsumerService(connectionProvider, options, logger)
{
    protected override string ConsumerName => "order-created-consumer";

    protected override async Task<string> DeclareQueueAsync(IChannel channel, CancellationToken ct)
    {
        await RabbitMqTopology.DeclareOrderCreatedQueueAsync(channel, ct);
        return MessagingTopology.OrderCreatedQueue;
    }

    protected override async Task HandleAsync(IntegrationEvent message, CancellationToken ct)
    {
        if (message.EventType != EventTypes.OrderCreated)
        {
            throw new InvalidOperationException($"EventType inesperado '{message.EventType}' na fila de pedidos criados.");
        }

        // Um escopo (e um DbContext) por mensagem: o consumo e concorrente.
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OrderProcessor>();
        await processor.ProcessOrderCreatedAsync(message, ct);
    }
}

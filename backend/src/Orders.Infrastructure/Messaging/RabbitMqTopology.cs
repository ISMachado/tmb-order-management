using Orders.Contracts;
using RabbitMQ.Client;

namespace Orders.Infrastructure.Messaging;

/// <summary>
/// Declara exchanges e filas. As declaracoes sao idempotentes, entao API e Worker podem chama-las ao subir
/// sem depender de ordem de inicializacao.
/// </summary>
public static class RabbitMqTopology
{
    /// <summary>Apos 5 entregas sem sucesso a mensagem vai para a DLQ (protecao contra "mensagem venenosa").</summary>
    public const int DeliveryLimit = 5;

    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(MessagingTopology.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
    }

    public static async Task DeclareOrderCreatedQueueAsync(IChannel channel, CancellationToken ct)
    {
        await DeclareExchangesAsync(channel, ct);

        // Fila de mensagens que falharam definitivamente, para inspecao/reprocessamento manual.
        await channel.QueueDeclareAsync(
            MessagingTopology.OrderCreatedDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: ct);
        await channel.QueueBindAsync(
            MessagingTopology.OrderCreatedDeadLetterQueue,
            MessagingTopology.DeadLetterExchange,
            MessagingTopology.OrderCreatedRoutingKey,
            cancellationToken: ct);

        // Quorum queue: replicada, duravel e com limite nativo de entregas.
        await channel.QueueDeclareAsync(
            MessagingTopology.OrderCreatedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = DeliveryLimit,
                ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = MessagingTopology.OrderCreatedRoutingKey,
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(
            MessagingTopology.OrderCreatedQueue,
            MessagingTopology.EventsExchange,
            MessagingTopology.OrderCreatedRoutingKey,
            cancellationToken: ct);
    }
}

using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Orders.Contracts;
using Orders.Infrastructure.Observability;
using Orders.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace Orders.Infrastructure.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// Publica mensagens com publisher confirms: o metodo so retorna depois que o broker confirmou que
/// recebeu (e persistiu) a mensagem. Se o broker recusar, lanca excecao e o outbox tenta de novo.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnectionProvider connectionProvider) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        // Continua o trace de quem criou o evento (ex.: a requisicao POST /orders).
        ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parent);
        using var activity = Telemetry.Source.StartActivity($"{message.RoutingKey} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", MessagingTopology.EventsExchange);
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        activity?.SetTag("messaging.message.conversation_id", message.CorrelationId.ToString());
        activity?.SetTag("orders.event_type", message.EventType);

        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId.ToString(),
            Type = message.EventType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [MessageHeaders.EventType] = message.EventType,
                [MessageHeaders.CorrelationId] = message.CorrelationId.ToString(),
            },
        };

        if (activity is not null)
        {
            Propagators.DefaultTextMapPropagator.Inject(
                new PropagationContext(activity.Context, Baggage.Current),
                properties.Headers,
                static (headers, key, value) => headers[key] = value);
        }

        var body = Encoding.UTF8.GetBytes(message.Payload);

        // OrderCreated precisa chegar a uma fila (mandatory); eventos de status sao "best effort" para o tempo real.
        var mandatory = message.EventType == EventTypes.OrderCreated;

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(MessagingTopology.EventsExchange, message.RoutingKey, mandatory, properties, body, ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            _channel.Dispose();
        }

        var connection = await connectionProvider.GetConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await RabbitMqTopology.DeclareOrderCreatedQueueAsync(channel, ct);
        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        var channel = Interlocked.Exchange(ref _channel, null);
        if (channel is not null)
        {
            try
            {
                await channel.CloseAsync();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or RabbitMQ.Client.Exceptions.AlreadyClosedException)
            {
                // Conexao ja encerrada durante o desligamento.
            }

            channel.Dispose();
        }

        _lock.Dispose();
    }
}

/// <summary>
/// Headers exigidos pelo desafio (CorrelationId = OrderId, EventType = OrderCreated). Os mesmos valores
/// tambem vao nas propriedades nativas do AMQP (correlation_id, type) e no corpo da mensagem.
/// </summary>
public static class MessageHeaders
{
    public const string EventType = "EventType";
    public const string CorrelationId = "CorrelationId";
}

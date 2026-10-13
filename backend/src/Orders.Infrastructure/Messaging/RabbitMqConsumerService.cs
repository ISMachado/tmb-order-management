using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Context.Propagation;
using Orders.Contracts;
using Orders.Infrastructure.Observability;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Orders.Infrastructure.Messaging;

/// <summary>
/// Base para consumidores RabbitMQ com: ack manual (so depois de processar), retry com backoff para
/// falhas transitorias, envio para DLQ quando a mensagem nao pode ser processada, e continuidade do trace.
/// </summary>
public abstract class RabbitMqConsumerService(
    RabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options,
    ILogger logger) : BackgroundService
{
    private readonly ResiliencePipeline _retry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException and not JsonException),
        })
        .Build();

    private IChannel? _channel;

    protected abstract string ConsumerName { get; }

    /// <summary>Declara a fila e devolve o nome dela.</summary>
    protected abstract Task<string> DeclareQueueAsync(IChannel channel, CancellationToken ct);

    protected abstract Task HandleAsync(IntegrationEvent message, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                await WatchConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Consumer}: falha no consumo; nova tentativa em 5s.", ConsumerName);
            }

            await DiscardChannelAsync();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool _consumerCancelled;

    /// <summary>
    /// Vigia o consumo. A recuperacao automatica do client cobre queda de CONEXAO; ja um canal fechado pelo broker
    /// (erro de protocolo, fila apagada, consumer cancelado) nao volta sozinho e, sem isto, o worker pararia de
    /// consumir silenciosamente. Se o canal ficar fechado por mais de 30s (ou o consumer for cancelado), recriamos tudo.
    /// </summary>
    private async Task WatchConsumerAsync(CancellationToken stoppingToken)
    {
        DateTime? closedSince = null;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            if (_consumerCancelled)
            {
                logger.LogWarning("{Consumer}: consumer cancelado pelo broker; recriando.", ConsumerName);
                return;
            }

            if (_channel is { IsOpen: true })
            {
                closedSince = null;
                continue;
            }

            closedSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - closedSince > TimeSpan.FromSeconds(30))
            {
                logger.LogWarning("{Consumer}: canal fechado ha mais de 30s; recriando.", ConsumerName);
                return;
            }
        }
    }

    private async Task DiscardChannelAsync()
    {
        var channel = Interlocked.Exchange(ref _channel, null);
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.CloseAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AlreadyClosedException or OperationCanceledException)
        {
            // Ja encerrado.
        }
        finally
        {
            channel.Dispose();
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        _consumerCancelled = false;
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        var prefetch = options.Value.PrefetchCount;
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: prefetch),
            stoppingToken);
        _channel = channel;

        var queue = await DeclareQueueAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        // O canal e capturado em variavel local: _channel e zerado no desligamento.
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, stoppingToken);
        consumer.UnregisteredAsync += (_, _) =>
        {
            _consumerCancelled = true;
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("{Consumer}: consumindo a fila {Queue} (prefetch {Prefetch}).", ConsumerName, queue, prefetch);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var parent = Propagators.DefaultTextMapPropagator.Extract(default, delivery.BasicProperties.Headers, ExtractHeader);
        using var activity = Telemetry.Source.StartActivity($"{delivery.RoutingKey} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.consumer.group.name", ConsumerName);
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
        activity?.SetTag("messaging.message.conversation_id", delivery.BasicProperties.CorrelationId);
        activity?.SetTag("messaging.rabbitmq.delivery.redelivered", delivery.Redelivered);

        using var _ = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = delivery.BasicProperties.MessageId,
            ["CorrelationId"] = delivery.BasicProperties.CorrelationId,
            ["EventType"] = delivery.BasicProperties.Type,
        });

        try
        {
            var message = JsonSerializer.Deserialize<IntegrationEvent>(delivery.Body.Span, MessagingJson.Options)
                ?? throw new JsonException("Mensagem vazia.");

            await _retry.ExecuteAsync(async ct => await HandleAsync(message, ct), stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando: devolve para a fila; outro worker (ou este, ao voltar) retoma de onde parou.
            await TryNackAsync(channel, delivery.DeliveryTag, requeue: true);
        }
        catch (Exception ex)
        {
            // Falha nao recuperavel (ou retries esgotados): vai para a DLQ em vez de travar a fila.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "{Consumer}: mensagem {MessageId} enviada para a DLQ.", ConsumerName, delivery.BasicProperties.MessageId);
            await TryNackAsync(channel, delivery.DeliveryTag, requeue: false);
        }
    }

    private async Task TryNackAsync(IChannel channel, ulong deliveryTag, bool requeue)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Canal fechado: o broker devolve a mensagem para a fila automaticamente.
            logger.LogWarning(ex, "{Consumer}: nao foi possivel enviar nack (canal fechado?).", ConsumerName);
        }
    }

    private static IEnumerable<string> ExtractHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is not null && headers.TryGetValue(key, out var value))
        {
            switch (value)
            {
                case byte[] bytes:
                    yield return Encoding.UTF8.GetString(bytes);
                    break;
                case string text:
                    yield return text;
                    break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Idempotente: so quem conseguir "pegar" o canal o fecha (o host pode chamar StopAsync mais de uma vez).
        var channel = Interlocked.Exchange(ref _channel, null);
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.CloseAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AlreadyClosedException or OperationCanceledException)
        {
            // Canal/conexao ja encerrados: nada a fazer, o broker devolve mensagens nao confirmadas para a fila.
            logger.LogDebug(ex, "{Consumer}: canal ja estava encerrado.", ConsumerName);
        }
        finally
        {
            channel.Dispose();
        }
    }
}

using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;

    /// <summary>Intervalo maximo entre varreduras (o relay tambem e acordado na hora quando um evento e gravado).</summary>
    public int PollingIntervalMs { get; set; } = 1000;

    public int MaxBackoffSeconds { get; set; } = 60;
}

/// <summary>Sinal em memoria para acordar o relay assim que um evento e gravado no outbox.</summary>
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout do polling: segue para a proxima varredura.
        }
    }
}

/// <summary>
/// Relay do Transactional Outbox: le eventos pendentes e publica no RabbitMQ.
/// <c>FOR UPDATE SKIP LOCKED</c> permite varias instancias rodando sem publicar o mesmo evento duas vezes
/// em paralelo. A garantia e "at-least-once": se cair entre publicar e marcar como enviado, o evento sera
/// reenviado - por isso o consumidor e idempotente.
/// </summary>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    OutboxSignal signal,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        logger.LogInformation("Outbox relay iniciado (lote {BatchSize}, polling {Interval} ms).", o.BatchSize, o.PollingIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishPendingBatchAsync(o, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do outbox relay; nova tentativa no proximo ciclo.");
            }

            // Lote cheio: provavelmente ha mais pendentes, continua sem esperar.
            if (published < o.BatchSize)
            {
                await signal.WaitAsync(TimeSpan.FromMilliseconds(o.PollingIntervalMs), stoppingToken);
            }
        }
    }

    public async Task<int> PublishPendingBatchAsync(OutboxOptions o, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow();
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                ORDER BY occurred_at
                LIMIT {o.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in messages)
        {
            try
            {
                // Broker travado (alarme de memoria/flow control) nao pode prender a transacao e os locks do lote.
                using var publishTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                publishTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await publisher.PublishAsync(message, publishTimeout.Token);
                message.ProcessedAt = time.GetUtcNow();
                message.LastError = null;
                published++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                var backoff = Math.Min(Math.Pow(2, message.Attempts), o.MaxBackoffSeconds);
                message.NextAttemptAt = time.GetUtcNow().AddSeconds(backoff);
                logger.LogWarning(ex, "Falha ao publicar {EventType} {MessageId} (tentativa {Attempts}); nova tentativa em {Backoff}s.",
                    message.EventType, message.Id, message.Attempts, backoff);

                // Broker provavelmente indisponivel: interrompe o lote para nao insistir em vao.
                // (A ordem entre eventos nao e garantida; os consumidores toleram isso - ver OrderProcessor e o frontend.)
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (published > 0)
        {
            logger.LogDebug("Outbox: {Count} evento(s) publicado(s).", published);
        }

        return published;
    }
}

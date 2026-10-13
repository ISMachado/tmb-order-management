using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orders.Application.Abstractions;
using Orders.Contracts;
using Orders.Domain;

namespace Orders.Application.Processing;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>Tempo entre Processando e Finalizado. O desafio pede 5 segundos.</summary>
    public int DelaySeconds { get; set; } = 5;
}

public enum ProcessingOutcome
{
    /// <summary>Pedido levado ate Finalizado por esta execucao.</summary>
    Completed,

    /// <summary>MessageId ja estava na inbox: mensagem duplicada, nada a fazer.</summary>
    Duplicate,

    /// <summary>Pedido ja estava Finalizado (ex.: outra mensagem para o mesmo pedido).</summary>
    AlreadyCompleted,

    /// <summary>Pedido inexistente: nao ha o que processar (mensagem descartada com log).</summary>
    OrderNotFound,
}

/// <summary>
/// Consumidor idempotente do evento OrderCreated.
/// <list type="bullet">
/// <item>Mensagem duplicada (mesmo MessageId) -> ignorada pela inbox.</item>
/// <item>Worker caiu durante os 5s -> na reentrega o pedido esta "Processando" e o worker retoma, esperando so o tempo restante.</item>
/// <item>Dois workers no mesmo pedido -> concorrencia otimista (xmin) garante uma unica transicao; o perdedor reavalia o estado.</item>
/// </list>
/// </summary>
public sealed class OrderProcessor(
    IOrdersDbContext db,
    IInbox inbox,
    IEventOutbox outbox,
    TimeProvider time,
    IOptions<ProcessingOptions> options,
    ILogger<OrderProcessor> logger)
{
    public const string ConsumerName = "order-created-processor";
    private const int MaxConflictRetries = 5;

    public async Task<ProcessingOutcome> ProcessOrderCreatedAsync(IntegrationEvent message, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryProcessAsync(message, ct);
            }
            catch (DbUpdateException ex) when (attempt < MaxConflictRetries)
            {
                // Outro consumidor alterou o pedido (ou registrou a mesma mensagem) entre a leitura e a escrita.
                // Descartamos o estado em memoria e reavaliamos a partir do banco.
                logger.LogWarning(ex, "Conflito ao processar pedido {OrderId} (tentativa {Attempt}); reavaliando estado.", message.CorrelationId, attempt);
                db.ResetTracking();
            }
        }
    }

    private async Task<ProcessingOutcome> TryProcessAsync(IntegrationEvent message, CancellationToken ct)
    {
        if (await inbox.HasProcessedAsync(message.MessageId, ConsumerName, ct))
        {
            logger.LogInformation("Mensagem {MessageId} ja processada; ignorando duplicata.", message.MessageId);
            return ProcessingOutcome.Duplicate;
        }

        var data = message.GetData<OrderCreatedData>();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == data.OrderId, ct);
        if (order is null)
        {
            logger.LogWarning("Pedido {OrderId} da mensagem {MessageId} nao encontrado; descartando.", data.OrderId, message.MessageId);
            inbox.MarkProcessed(message.MessageId, ConsumerName, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return ProcessingOutcome.OrderNotFound;
        }

        var outcome = order.Status == OrderStatus.Finalizado ? ProcessingOutcome.AlreadyCompleted : ProcessingOutcome.Completed;

        if (order.Status == OrderStatus.Pendente)
        {
            var processing = order.StartProcessing(time.GetUtcNow());
            outbox.Enqueue(EventTypes.OrderStatusChanged, order.Id, ToEventData(processing));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Pedido {OrderId}: Pendente -> Processando.", order.Id);
        }

        if (order.Status == OrderStatus.Processando)
        {
            await WaitRemainingProcessingTimeAsync(order, ct);

            var completed = order.Complete(time.GetUtcNow());
            outbox.Enqueue(EventTypes.OrderStatusChanged, order.Id, ToEventData(completed));
            logger.LogInformation("Pedido {OrderId}: Processando -> Finalizado.", order.Id);
        }

        // A inbox e gravada na mesma transacao do status final: ou tudo persiste, ou a mensagem e reprocessada.
        inbox.MarkProcessed(message.MessageId, ConsumerName, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return outcome;
    }

    private async Task WaitRemainingProcessingTimeAsync(Order order, CancellationToken ct)
    {
        var total = TimeSpan.FromSeconds(options.Value.DelaySeconds);
        var startedAt = order.ProcessamentoIniciadoEm ?? time.GetUtcNow();
        var remaining = total - (time.GetUtcNow() - startedAt);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, time, ct);
        }
    }

    private static OrderStatusChangedData ToEventData(OrderStatusHistory entry) =>
        new(entry.OrderId, entry.StatusAnterior?.ToString(), entry.StatusNovo.ToString(), entry.AlteradoEm);
}

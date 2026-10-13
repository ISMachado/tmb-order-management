using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Contracts;

namespace Orders.Infrastructure.Persistence;

internal sealed class EfEventOutbox(OrdersDbContext db, TimeProvider time) : IEventOutbox
{
    public void Enqueue<TData>(string eventType, Guid correlationId, TData data)
    {
        var now = time.GetUtcNow();
        var envelope = new IntegrationEvent(
            MessageId: Guid.CreateVersion7(now),
            EventType: eventType,
            CorrelationId: correlationId,
            OccurredAt: now,
            Data: JsonSerializer.SerializeToElement(data, MessagingJson.Options));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = envelope.MessageId,
            EventType = eventType,
            RoutingKey = MessagingTopology.RoutingKeyFor(eventType),
            CorrelationId = correlationId,
            Payload = JsonSerializer.Serialize(envelope, MessagingJson.Options),
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            OccurredAt = now,
        });
    }
}

internal sealed class EfInbox(OrdersDbContext db) : IInbox
{
    public Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken) =>
        db.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);

    public void MarkProcessed(Guid messageId, string consumer, DateTimeOffset processedAt) =>
        db.InboxMessages.Add(new InboxMessage { MessageId = messageId, Consumer = consumer, ProcessedAt = processedAt });
}

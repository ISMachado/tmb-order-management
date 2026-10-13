using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Contracts;
using Orders.Domain;

namespace Orders.Application.Orders;

/// <summary>Casos de uso de pedidos expostos pela API REST.</summary>
public sealed class OrderService(IOrdersDbContext db, IEventOutbox outbox, TimeProvider time)
{
    /// <summary>
    /// Cria o pedido (status Pendente) e grava o evento OrderCreated no outbox na MESMA transacao.
    /// Se a Idempotency-Key ja foi usada, devolve o pedido existente sem criar outro.
    /// </summary>
    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is not null && await FindByIdempotencyKeyAsync(idempotencyKey, ct) is { } existing)
        {
            return new CreateOrderResult(existing.ToResponse(), Created: false);
        }

        var order = Order.Create(request.Cliente, request.Produto, request.Valor, time.GetUtcNow(), idempotencyKey);
        db.Orders.Add(order);
        outbox.Enqueue(
            EventTypes.OrderCreated,
            correlationId: order.Id,
            new OrderCreatedData(order.Id, order.Cliente, order.Produto, order.Valor, order.DataCriacao));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // Duas requisicoes simultaneas com a mesma chave: o indice unico barrou a segunda.
            db.ResetTracking();
            var winner = await FindByIdempotencyKeyAsync(idempotencyKey, ct);
            if (winner is null)
            {
                throw;
            }

            return new CreateOrderResult(winner.ToResponse(), Created: false);
        }

        return new CreateOrderResult(order.ToResponse(), Created: true);
    }

    public async Task<PagedResponse<OrderResponse>> ListAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking();
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        var total = await orders.CountAsync(ct);
        var items = await orders
            .OrderByDescending(o => o.DataCriacao)
            .ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new OrderResponse(
                o.Id, o.Cliente, o.Produto, o.Valor, o.Status, o.DataCriacao, o.DataAtualizacao, o.ProcessamentoIniciadoEm, o.FinalizadoEm))
            .ToListAsync(ct);

        return new PagedResponse<OrderResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<OrderDetailsResponse?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return null;
        }

        var history = await db.OrderStatusHistory.AsNoTracking()
            .Where(h => h.OrderId == id)
            .OrderBy(h => h.AlteradoEm)
            .ThenBy(h => h.Id)
            .Select(h => new OrderStatusHistoryResponse(h.StatusAnterior, h.StatusNovo, h.AlteradoEm))
            .ToListAsync(ct);

        return new OrderDetailsResponse(
            order.Id, order.Cliente, order.Produto, order.Valor, order.Status, order.DataCriacao, order.DataAtualizacao,
            order.ProcessamentoIniciadoEm, order.FinalizadoEm, history);
    }

    private Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.IdempotencyKey == key, ct);
}

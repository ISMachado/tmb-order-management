using Orders.Domain;

namespace Orders.Application.Orders;

public sealed record CreateOrderRequest(string Cliente, string Produto, decimal Valor);

public sealed record OrderResponse(
    Guid Id,
    string Cliente,
    string Produto,
    decimal Valor,
    OrderStatus Status,
    DateTimeOffset DataCriacao,
    DateTimeOffset DataAtualizacao,
    DateTimeOffset? ProcessamentoIniciadoEm,
    DateTimeOffset? FinalizadoEm);

public sealed record OrderStatusHistoryResponse(
    OrderStatus? StatusAnterior,
    OrderStatus StatusNovo,
    DateTimeOffset AlteradoEm);

public sealed record OrderDetailsResponse(
    Guid Id,
    string Cliente,
    string Produto,
    decimal Valor,
    OrderStatus Status,
    DateTimeOffset DataCriacao,
    DateTimeOffset DataAtualizacao,
    DateTimeOffset? ProcessamentoIniciadoEm,
    DateTimeOffset? FinalizadoEm,
    IReadOnlyList<OrderStatusHistoryResponse> Historico);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record ListOrdersQuery(OrderStatus? Status = null, int Page = 1, int PageSize = ListOrdersQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

public sealed record CreateOrderResult(OrderResponse Order, bool Created);

internal static class OrderMappings
{
    public static OrderResponse ToResponse(this Order o) => new(
        o.Id, o.Cliente, o.Produto, o.Valor, o.Status, o.DataCriacao, o.DataAtualizacao, o.ProcessamentoIniciadoEm, o.FinalizadoEm);
}

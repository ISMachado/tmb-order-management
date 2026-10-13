using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Orders.Application.Orders;
using Orders.Domain;

namespace Orders.Api.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Pedidos");

        group.MapPost("/", CreateAsync)
            .WithName("CreateOrder")
            .WithSummary("Cria um novo pedido")
            .WithDescription("Persiste o pedido com status Pendente e agenda a publicacao do evento OrderCreated (Transactional Outbox). " +
                             "Aceita o header opcional Idempotency-Key para evitar duplicidade em reenvios.");

        group.MapGet("/", ListAsync)
            .WithName("ListOrders")
            .WithSummary("Lista os pedidos (mais recentes primeiro)")
            .WithDescription("Filtros opcionais: status, page e page_size.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetOrder")
            .WithSummary("Obtem os detalhes de um pedido, incluindo o historico de status");

        return app;
    }

    private static async Task<Results<Created<OrderResponse>, Ok<OrderResponse>, ValidationProblem>> CreateAsync(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        IValidator<CreateOrderRequest> validator,
        OrderService service,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        if (idempotencyKey is { Length: > 100 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyKeyHeader] = ["O header Idempotency-Key deve ter no maximo 100 caracteres."],
            });
        }

        var result = await service.CreateAsync(request, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey, ct);
        return result.Created
            ? TypedResults.Created($"/orders/{result.Order.Id}", result.Order)
            : TypedResults.Ok(result.Order);
    }

    private static async Task<Results<Ok<PagedResponse<OrderResponse>>, ValidationProblem>> ListAsync(
        [FromQuery] OrderStatus? status,
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        IValidator<ListOrdersQuery> validator,
        OrderService service,
        CancellationToken ct)
    {
        var query = new ListOrdersQuery(status, page ?? 1, pageSize ?? ListOrdersQuery.DefaultPageSize);
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        return TypedResults.Ok(await service.ListAsync(query, ct));
    }

    private static async Task<Results<Ok<OrderDetailsResponse>, NotFound<ProblemDetails>>> GetAsync(
        Guid id,
        OrderService service,
        CancellationToken ct)
    {
        var order = await service.GetByIdAsync(id, ct);
        return order is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Pedido nao encontrado",
                Detail = $"Nao existe pedido com id {id}.",
            })
            : TypedResults.Ok(order);
    }
}

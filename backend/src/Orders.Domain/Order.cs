namespace Orders.Domain;

/// <summary>
/// Agregado Pedido. Toda mudanca de status passa por <see cref="ChangeStatus"/>,
/// que valida a sequencia obrigatoria e registra o historico.
/// </summary>
public sealed class Order
{
    private readonly List<OrderStatusHistory> _history = [];

    // Construtor usado pelo EF Core.
    private Order()
    {
        Cliente = string.Empty;
        Produto = string.Empty;
    }

    private Order(Guid id, string cliente, string produto, decimal valor, DateTimeOffset now, string? idempotencyKey)
    {
        Id = id;
        Cliente = cliente;
        Produto = produto;
        Valor = valor;
        Status = OrderStatus.Pendente;
        DataCriacao = now;
        DataAtualizacao = now;
        IdempotencyKey = idempotencyKey;
        _history.Add(new OrderStatusHistory(id, null, OrderStatus.Pendente, now));
    }

    public Guid Id { get; private set; }
    public string Cliente { get; private set; }
    public string Produto { get; private set; }
    public decimal Valor { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset DataCriacao { get; private set; }
    public DateTimeOffset DataAtualizacao { get; private set; }
    public DateTimeOffset? ProcessamentoIniciadoEm { get; private set; }
    public DateTimeOffset? FinalizadoEm { get; private set; }

    /// <summary>Chave opcional enviada pelo cliente para evitar pedidos duplicados em reenvios.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Token de concorrencia otimista (mapeado para a coluna de sistema xmin do PostgreSQL).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<OrderStatusHistory> History => _history;

    public static Order Create(string cliente, string produto, decimal valor, DateTimeOffset now, string? idempotencyKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliente);
        ArgumentException.ThrowIfNullOrWhiteSpace(produto);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valor);

        return new Order(Guid.CreateVersion7(now), cliente.Trim(), produto.Trim(), valor, now, idempotencyKey);
    }

    public OrderStatusHistory StartProcessing(DateTimeOffset now)
    {
        var entry = ChangeStatus(OrderStatus.Processando, now);
        ProcessamentoIniciadoEm = now;
        return entry;
    }

    public OrderStatusHistory Complete(DateTimeOffset now)
    {
        var entry = ChangeStatus(OrderStatus.Finalizado, now);
        FinalizadoEm = now;
        return entry;
    }

    private OrderStatusHistory ChangeStatus(OrderStatus to, DateTimeOffset now)
    {
        if (!OrderStatusTransitions.CanTransition(Status, to))
        {
            throw new InvalidStatusTransitionException(Id, Status, to);
        }

        var entry = new OrderStatusHistory(Id, Status, to, now);
        _history.Add(entry);
        Status = to;
        DataAtualizacao = now;
        return entry;
    }
}

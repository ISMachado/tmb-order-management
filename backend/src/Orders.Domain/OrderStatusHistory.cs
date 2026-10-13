namespace Orders.Domain;

/// <summary>Uma linha do historico de status de um pedido (auditoria do ciclo de vida).</summary>
public sealed class OrderStatusHistory
{
    // Construtor usado pelo EF Core.
    private OrderStatusHistory()
    {
    }

    internal OrderStatusHistory(Guid orderId, OrderStatus? statusAnterior, OrderStatus statusNovo, DateTimeOffset alteradoEm)
    {
        OrderId = orderId;
        StatusAnterior = statusAnterior;
        StatusNovo = statusNovo;
        AlteradoEm = alteradoEm;
    }

    public long Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? StatusAnterior { get; private set; }
    public OrderStatus StatusNovo { get; private set; }
    public DateTimeOffset AlteradoEm { get; private set; }
}

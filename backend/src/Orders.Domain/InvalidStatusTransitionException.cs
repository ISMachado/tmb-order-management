namespace Orders.Domain;

public sealed class InvalidStatusTransitionException : Exception
{
    public InvalidStatusTransitionException(Guid orderId, OrderStatus from, OrderStatus to)
        : base($"Pedido {orderId}: transicao de status invalida de {from} para {to}. Sequencia obrigatoria: Pendente -> Processando -> Finalizado.")
    {
        OrderId = orderId;
        From = from;
        To = to;
    }

    public Guid OrderId { get; }
    public OrderStatus From { get; }
    public OrderStatus To { get; }
}

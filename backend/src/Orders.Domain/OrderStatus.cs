namespace Orders.Domain;

/// <summary>
/// Status possiveis de um pedido. O valor numerico define a ordem do ciclo de vida,
/// o que permite comparar "quem esta mais adiante" (usado pelo frontend para ignorar eventos atrasados).
/// </summary>
public enum OrderStatus
{
    Pendente = 1,
    Processando = 2,
    Finalizado = 3,
}

public static class OrderStatusTransitions
{
    /// <summary>
    /// Unica sequencia permitida: Pendente -> Processando -> Finalizado.
    /// </summary>
    public static bool CanTransition(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pendente, OrderStatus.Processando) => true,
        (OrderStatus.Processando, OrderStatus.Finalizado) => true,
        _ => false,
    };
}

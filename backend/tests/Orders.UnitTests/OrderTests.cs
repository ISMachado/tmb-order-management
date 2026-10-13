using Orders.Domain;

namespace Orders.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_order_is_Pendente_with_initial_history_entry()
    {
        var order = Order.Create("  Ana  ", " Notebook ", 1500.50m, Now);

        Assert.Equal(OrderStatus.Pendente, order.Status);
        Assert.Equal("Ana", order.Cliente);
        Assert.Equal("Notebook", order.Produto);
        Assert.Equal(Now, order.DataCriacao);
        var entry = Assert.Single(order.History);
        Assert.Null(entry.StatusAnterior);
        Assert.Equal(OrderStatus.Pendente, entry.StatusNovo);
    }

    [Fact]
    public void Order_follows_the_mandatory_sequence_and_records_each_step()
    {
        var order = Order.Create("Ana", "Notebook", 10m, Now);

        order.StartProcessing(Now.AddSeconds(1));
        order.Complete(Now.AddSeconds(6));

        Assert.Equal(OrderStatus.Finalizado, order.Status);
        Assert.Equal(Now.AddSeconds(1), order.ProcessamentoIniciadoEm);
        Assert.Equal(Now.AddSeconds(6), order.FinalizadoEm);
        Assert.Equal(
            [OrderStatus.Pendente, OrderStatus.Processando, OrderStatus.Finalizado],
            order.History.Select(h => h.StatusNovo));
    }

    [Fact]
    public void Cannot_skip_Processando()
    {
        var order = Order.Create("Ana", "Notebook", 10m, Now);

        var ex = Assert.Throws<InvalidStatusTransitionException>(() => order.Complete(Now));

        Assert.Equal(OrderStatus.Pendente, ex.From);
        Assert.Equal(OrderStatus.Finalizado, ex.To);
        Assert.Equal(OrderStatus.Pendente, order.Status);
    }

    [Fact]
    public void Cannot_start_processing_twice_or_go_backwards()
    {
        var order = Order.Create("Ana", "Notebook", 10m, Now);
        order.StartProcessing(Now);

        Assert.Throws<InvalidStatusTransitionException>(() => order.StartProcessing(Now));

        order.Complete(Now);
        Assert.Throws<InvalidStatusTransitionException>(() => order.Complete(Now));
        Assert.Throws<InvalidStatusTransitionException>(() => order.StartProcessing(Now));
    }

    [Theory]
    [InlineData(OrderStatus.Pendente, OrderStatus.Processando, true)]
    [InlineData(OrderStatus.Processando, OrderStatus.Finalizado, true)]
    [InlineData(OrderStatus.Pendente, OrderStatus.Finalizado, false)]
    [InlineData(OrderStatus.Processando, OrderStatus.Pendente, false)]
    [InlineData(OrderStatus.Finalizado, OrderStatus.Processando, false)]
    [InlineData(OrderStatus.Finalizado, OrderStatus.Pendente, false)]
    [InlineData(OrderStatus.Pendente, OrderStatus.Pendente, false)]
    public void Transition_table_only_allows_the_mandatory_sequence(OrderStatus from, OrderStatus to, bool allowed) =>
        Assert.Equal(allowed, OrderStatusTransitions.CanTransition(from, to));

    [Theory]
    [InlineData("", "p", 1)]
    [InlineData("c", " ", 1)]
    [InlineData("c", "p", 0)]
    [InlineData("c", "p", -1)]
    public void Invalid_data_is_rejected_by_the_domain(string cliente, string produto, decimal valor) =>
        Assert.ThrowsAny<ArgumentException>(() => Order.Create(cliente, produto, valor, Now));

    [Fact]
    public void Order_ids_are_time_ordered()
    {
        var first = Order.Create("a", "p", 1m, Now);
        var second = Order.Create("a", "p", 1m, Now.AddSeconds(1));

        Assert.True(first.Id.CompareTo(second.Id) < 0);
    }
}

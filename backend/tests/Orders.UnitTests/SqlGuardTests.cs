using Orders.Infrastructure.Ai;

namespace Orders.UnitTests;

public class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT COUNT(*) FROM vw_pedidos")]
    [InlineData("select status, count(*) from vw_pedidos group by status;")]
    [InlineData("SELECT AVG(EXTRACT(EPOCH FROM (finalizado_em - data_criacao))) FROM vw_pedidos WHERE status = 'Finalizado'")]
    [InlineData("SELECT p.cliente, h.status_novo FROM vw_pedidos p JOIN vw_historico_status h ON h.pedido_id = p.id")]
    [InlineData("WITH recentes AS (SELECT * FROM vw_pedidos) SELECT COUNT(*) FROM recentes")]
    public void Safe_selects_are_accepted(string sql)
    {
        Assert.True(SqlGuard.TryValidate(sql, out var normalized, out var error), error);
        Assert.DoesNotContain(';', normalized);
    }

    [Theory]
    [InlineData("DELETE FROM vw_pedidos")]
    [InlineData("UPDATE orders SET valor = 0")]
    [InlineData("INSERT INTO orders VALUES (1)")]
    [InlineData("DROP TABLE orders")]
    [InlineData("SELECT * FROM vw_pedidos; DROP TABLE orders")]
    [InlineData("SELECT * FROM orders")]
    [InlineData("SELECT * FROM outbox_messages")]
    [InlineData("SELECT * FROM pg_catalog.pg_user")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT pg_sleep(100)")]
    [InlineData("SELECT * FROM vw_pedidos -- comentario")]
    [InlineData("SELECT * FROM vw_pedidos /* x */")]
    [InlineData("SELECT * INTO novo FROM vw_pedidos")]
    [InlineData("COPY vw_pedidos TO '/tmp/x'")]
    [InlineData("SET ROLE postgres")]
    [InlineData("SELECT current_setting('is_superuser')")]
    [InlineData("")]
    [InlineData("   ")]
    public void Dangerous_or_out_of_scope_statements_are_rejected(string sql)
    {
        Assert.False(SqlGuard.TryValidate(sql, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Row_limit_wraps_the_query()
    {
        var wrapped = SqlGuard.WithRowLimit("SELECT 1");

        Assert.Equal($"SELECT * FROM (SELECT 1) AS resultado LIMIT {SqlGuard.MaxRows}", wrapped);
    }
}

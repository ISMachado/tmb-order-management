using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orders.Domain;
using Orders.Infrastructure.Ai;
using Orders.TestSupport;

namespace Orders.IntegrationTests;

/// <summary>
/// Modulo de IA contra o banco real, usando o usuario SOMENTE LEITURA. A LLM e substituida por um cliente falso
/// que "decide" chamar ferramentas; assim validamos tudo que e nosso: ferramentas, consultas, seguranca e periodos.
/// </summary>
[Collection(OrdersCollectionNames.Environment)]
public sealed class AiModuleTests(OrdersFixture env) : IAsyncLifetime
{
    private NpgsqlDataSource _readOnly = null!;
    private AnalyticsQueries _queries = null!;
    private readonly string _tag = "ai-" + Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync()
    {
        _readOnly = NpgsqlDataSource.Create(env.ReadOnlyConnectionString);
        _queries = new AnalyticsQueries(_readOnly);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _readOnly.DisposeAsync();

    private async Task SeedAsync()
    {
        await using var db = env.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        // 3 finalizados (R$100, R$200, R$300) levando 5s, 10s e 15s; 2 pendentes (R$50 cada).
        foreach (var (valor, seconds) in new[] { (100m, 5), (200m, 10), (300m, 15) })
        {
            var order = Order.Create(_tag, "Produto AI", valor, now.AddMinutes(-10));
            order.StartProcessing(now.AddMinutes(-10).AddMilliseconds(100));
            order.Complete(now.AddMinutes(-10).AddSeconds(seconds));
            db.Orders.Add(order);
        }

        for (var i = 0; i < 2; i++)
        {
            db.Orders.Add(Order.Create(_tag, "Produto AI", 50m, now.AddMinutes(-1)));
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Analytics_queries_return_correct_real_data()
    {
        await SeedAsync();
        var mine = new OrderFilter(null, null, null, Cliente: _tag);

        Assert.Equal(5, (await _queries.CountAsync(mine, default)).Total);
        Assert.Equal(2, (await _queries.CountAsync(mine with { Status = "Pendente" }, default)).Total);

        var sum = await _queries.SumAsync(mine with { Status = "Finalizado" }, default);
        Assert.Equal(600m, sum.Total);
        Assert.Equal(3, sum.Pedidos);

        var (stats, _) = await _queries.ProcessingTimeAsync(mine, default);
        Assert.Equal(3L, Convert.ToInt64(stats["pedidos_finalizados"]));
        Assert.InRange(Convert.ToDecimal(stats["media_segundos"]), 9.9m, 10.1m); // (5+10+15)/3
        Assert.Equal(5m, Math.Round(Convert.ToDecimal(stats["minimo_segundos"]), 0));
        Assert.Equal(15m, Math.Round(Convert.ToDecimal(stats["maximo_segundos"]), 0));

        var (groups, _) = await _queries.GroupByStatusAsync(mine, default);
        Assert.Contains(groups, g => (string)g["status"]! == "Pendente" && Convert.ToInt64(g["pedidos"]) == 2);
    }

    [Fact]
    public async Task Period_filters_use_the_requested_date_range()
    {
        await SeedAsync();
        var mine = new OrderFilter(null, null, null, Cliente: _tag);

        var future = mine with { From = DateTimeOffset.UtcNow.AddDays(1), To = DateTimeOffset.UtcNow.AddDays(2) };
        var lastHour = mine with { From = DateTimeOffset.UtcNow.AddHours(-1), To = DateTimeOffset.UtcNow.AddMinutes(1) };

        Assert.Equal(0, (await _queries.CountAsync(future, default)).Total);
        Assert.Equal(5, (await _queries.CountAsync(lastHour, default)).Total);
    }

    [Fact]
    public async Task Parameter_values_are_never_interpolated_into_sql()
    {
        await SeedAsync();

        // Tentativa classica de injecao no filtro de cliente: deve ser tratada como texto, nao como SQL.
        var (total, sql) = await _queries.CountAsync(new OrderFilter(null, null, null, Cliente: "'; DROP TABLE orders; --"), default);

        Assert.Equal(0, total);
        Assert.DoesNotContain("DROP", sql, StringComparison.OrdinalIgnoreCase);
        await using var db = env.CreateDbContext();
        Assert.True(await db.Orders.AnyAsync());
    }

    [Fact]
    public async Task Read_only_role_cannot_write_or_read_base_tables_even_if_sql_passed_the_guard()
    {
        await SeedAsync();

        // SqlGuard bloqueia na entrada; aqui garantimos a defesa do BANCO caso algo passe pelo guard.
        await Assert.ThrowsAnyAsync<PostgresException>(() => _queries.RunReadOnlyAsync("SELECT * FROM orders", default));
        await using var connection = await _readOnly.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("UPDATE vw_pedidos SET valor = 1", connection);
        await Assert.ThrowsAnyAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Free_sql_tool_is_limited_to_100_rows_and_reads_only_the_views()
    {
        var rows = await _queries.RunReadOnlyAsync("SELECT generate_series(1, 500) AS n", default);

        Assert.Equal(SqlGuard.MaxRows, rows.Count);
    }

    [Fact]
    public async Task Assistant_answers_using_tools_that_hit_the_real_database()
    {
        await SeedAsync();
        var fake = new ScriptedChatClient(
            toolName: "contar_pedidos",
            arguments: new Dictionary<string, object?> { ["status"] = "Pendente", ["periodo"] = "todos" });
        var assistant = CreateAssistant(fake);

        var result = await assistant.AskAsync("Quantos pedidos estao pendentes?", default);

        Assert.Contains("total_pedidos", fake.LastToolResult);
        var query = Assert.Single(result.ConsultasExecutadas);
        Assert.Equal("contar_pedidos", query.Ferramenta);
        Assert.Contains("vw_pedidos", query.Sql);
        Assert.Contains("@status", query.Sql); // parametrizado
        Assert.Contains(DateTimeOffset.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), fake.SystemPrompt); // data atual no prompt
    }

    [Fact]
    public async Task Assistant_free_sql_tool_rejects_dangerous_sql_and_reports_the_error_to_the_llm()
    {
        var fake = new ScriptedChatClient("consulta_sql_somente_leitura", new Dictionary<string, object?> { ["sql"] = "DELETE FROM vw_pedidos" });
        var assistant = CreateAssistant(fake);
        await using var db = env.CreateDbContext();
        var before = await db.Orders.CountAsync();

        var result = await assistant.AskAsync("apague tudo", default);

        Assert.Contains("erro", fake.LastToolResult, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.Single(result.ConsultasExecutadas).Sql); // nada foi executado
        Assert.Equal(before, await db.Orders.CountAsync()); // nenhum pedido foi apagado
    }

    [Theory]
    [InlineData("contar_pedidos", "status", "Cancelado")]
    [InlineData("contar_pedidos", "periodo", "semana_que_vem")]
    [InlineData("contar_pedidos", "dataInicio", "ontem")]
    public async Task Invalid_tool_input_is_reported_to_the_llm_instead_of_silently_removing_the_filter(string tool, string argument, string value)
    {
        var fake = new ScriptedChatClient(tool, new Dictionary<string, object?> { [argument] = value });
        var assistant = CreateAssistant(fake);

        var result = await assistant.AskAsync("quantos pedidos cancelados?", default);

        Assert.Contains("erro", fake.LastToolResult, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.ConsultasExecutadas); // nenhuma consulta foi executada com filtro "afrouxado"
    }

    [Fact]
    public async Task Sql_errors_from_the_llm_are_returned_to_it_so_it_can_retry()
    {
        var fake = new ScriptedChatClient("consulta_sql_somente_leitura", new Dictionary<string, object?> { ["sql"] = "SELECT coluna_que_nao_existe FROM vw_pedidos" });
        var assistant = CreateAssistant(fake);

        var answer = await assistant.AskAsync("pergunta", default); // nao lanca AiProviderException

        Assert.Contains("A consulta falhou", fake.LastToolResult);
        Assert.NotEmpty(answer.Resposta);
    }

    [Fact]
    public async Task Assistant_is_disabled_without_a_chat_client()
    {
        var assistant = new OrdersAssistant(_queries, TimeProvider.System, Options.Create(new AiOptions()), NullLogger<OrdersAssistant>.Instance, chatClient: null);

        Assert.False(assistant.IsEnabled);
        await Assert.ThrowsAsync<Orders.Application.Ai.AiDisabledException>(() => assistant.AskAsync("oi", default));
    }

    private OrdersAssistant CreateAssistant(IChatClient inner)
    {
        // Mesma composicao da producao: o cliente da LLM embrulhado com invocacao automatica de ferramentas.
        var client = new ChatClientBuilder(inner).UseFunctionInvocation().Build();
        return new OrdersAssistant(_queries, TimeProvider.System, Options.Create(new AiOptions { Model = "fake" }), NullLogger<OrdersAssistant>.Instance, client);
    }

    /// <summary>LLM falsa: na 1a chamada pede uma ferramenta; na 2a, responde usando o resultado dela.</summary>
    private sealed class ScriptedChatClient(string toolName, IDictionary<string, object?> arguments) : IChatClient
    {
        public string LastToolResult { get; private set; } = string.Empty;

        public string SystemPrompt { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            SystemPrompt = list.First(m => m.Role == ChatRole.System).Text;

            var toolResult = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().FirstOrDefault();
            if (toolResult is null)
            {
                Assert.Contains(options!.Tools!, t => t.Name == toolName);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", toolName, arguments)])));
            }

            LastToolResult = System.Text.Json.JsonSerializer.Serialize(toolResult.Result);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Resposta baseada em: {LastToolResult}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

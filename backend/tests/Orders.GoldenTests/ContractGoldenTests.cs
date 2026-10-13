using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orders.Contracts;
using Orders.Infrastructure.Ai;
using Orders.TestSupport;

namespace Orders.GoldenTests;

/// <summary>
/// Golden tests: o contrato publico do sistema e "congelado" em arquivos *.verified.*.
/// Qualquer mudanca acidental (nome de campo, formato de mensagem, endpoint, ferramenta da IA) quebra o teste
/// e mostra o diff; mudancas intencionais sao aceitas atualizando o snapshot de forma consciente.
/// </summary>
[Collection(OrdersCollectionNames.Environment)]
public sealed class ContractGoldenTests(OrdersFixture env)
{
    private static readonly Guid OrderId = Guid.Parse("01a12147-bc07-7fd3-b1cb-32272237a043");
    private static readonly Guid MessageId = Guid.Parse("11111111-2222-7333-8444-555555555555");
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 28, 31, TimeSpan.Zero);

    [Fact]
    public Task OrderCreated_message_envelope()
    {
        var envelope = new IntegrationEvent(
            MessageId,
            EventTypes.OrderCreated,
            CorrelationId: OrderId,
            At,
            JsonSerializer.SerializeToElement(new OrderCreatedData(OrderId, "Ana Souza", "Notebook Pro 14", 7899.90m, At), MessagingJson.Options));

        return VerifyJson(JsonSerializer.Serialize(envelope, MessagingJson.Options));
    }

    [Fact]
    public Task OrderStatusChanged_message_envelope()
    {
        var envelope = new IntegrationEvent(
            MessageId,
            EventTypes.OrderStatusChanged,
            CorrelationId: OrderId,
            At,
            JsonSerializer.SerializeToElement(new OrderStatusChangedData(OrderId, "Pendente", "Processando", At), MessagingJson.Options));

        return VerifyJson(JsonSerializer.Serialize(envelope, MessagingJson.Options));
    }

    [Fact]
    public async Task Create_order_response()
    {
        var response = await env.Api.CreateClient().PostAsJsonAsync("/orders", new { cliente = "Golden Cliente", produto = "Golden Produto", valor = 1299.90 });

        await VerifyJson(await response.Content.ReadAsStringAsync()).UseParameters((int)response.StatusCode);
    }

    [Fact]
    public async Task Order_details_response_after_the_worker_finishes()
    {
        var client = env.Api.CreateClient();
        var created = await (await client.PostAsJsonAsync("/orders", new { cliente = "Golden Detalhes", produto = "Golden Produto", valor = 10 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var json = await Eventually.ReturnsAsync(
            () => client.GetStringAsync($"/orders/{id}"),
            body => JsonDocument.Parse(body).RootElement.GetProperty("status").GetString() == "Finalizado");

        await VerifyJson(json);
    }

    [Fact]
    public async Task Validation_error_response()
    {
        var response = await env.Api.CreateClient().PostAsJsonAsync("/orders", new { cliente = "", produto = "", valor = -1 });

        await VerifyJson(await response.Content.ReadAsStringAsync()).UseParameters((int)response.StatusCode);
    }

    [Fact]
    public async Task Not_found_response()
    {
        var response = await env.Api.CreateClient().GetAsync($"/orders/{OrderId}");

        await VerifyJson(await response.Content.ReadAsStringAsync()).UseParameters((int)response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_response_shape()
    {
        var json = await env.Api.CreateClient().GetStringAsync("/health/ready");

        // Duracoes e a porta do container mudam a cada execucao: o que importa e o formato e os status.
        var node = JsonNode.Parse(json)!;
        node.AsObject().Remove("totalDuration");
        foreach (var entry in node["entries"]!.AsObject())
        {
            entry.Value!.AsObject().Remove("duration");
            entry.Value.AsObject().Remove("description");
        }

        await VerifyJson(node.ToJsonString());
    }

    [Fact]
    public async Task OpenApi_document()
    {
        var json = await env.Api.CreateClient().GetStringAsync("/openapi/v1.json");

        await VerifyJson(json);
    }

    [Fact]
    public async Task Ai_tool_definitions_sent_to_the_llm()
    {
        await using var dataSource = NpgsqlDataSource.Create(env.ReadOnlyConnectionString);
        var capture = new CapturingChatClient();
        var assistant = new OrdersAssistant(
            new AnalyticsQueries(dataSource),
            TimeProvider.System,
            Options.Create(new AiOptions { Model = "fake" }),
            NullLogger<OrdersAssistant>.Instance,
            capture);

        await assistant.AskAsync("qualquer pergunta", default);

        var tools = capture.Tools.OfType<AIFunction>()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new { t.Name, t.Description, Schema = t.JsonSchema })
            .ToList();
        await VerifyJson(JsonSerializer.Serialize(tools));
    }

    [Fact]
    public async Task Ai_system_prompt_for_a_fixed_date()
    {
        var prompt = OrdersAssistant.BuildSystemPrompt(new DateTimeOffset(2026, 10, 9, 22, 30, 0, TimeSpan.FromHours(-3)));

        await Verify(prompt);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IList<AITool> Tools { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Tools = options?.Tools ?? [];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

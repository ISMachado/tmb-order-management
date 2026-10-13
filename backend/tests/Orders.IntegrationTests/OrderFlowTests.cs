using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orders.Contracts;
using Orders.Domain;
using Orders.Infrastructure.Persistence;
using Orders.TestSupport;
using RabbitMQ.Client;

namespace Orders.IntegrationTests;

[Collection(OrdersCollectionNames.Environment)]
public sealed class OrderFlowTests(OrdersFixture env)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private HttpClient Client => env.Api.CreateClient();

    private async Task<(Guid Id, JsonElement Body)> CreateOrderAsync(string cliente = "Cliente Teste", decimal valor = 150.50m, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { cliente, produto = "Produto Teste", valor }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body);
    }

    private async Task<JsonElement> GetOrderAsync(Guid id) =>
        await Client.GetFromJsonAsync<JsonElement>($"/orders/{id}");

    [Fact]
    public async Task Create_order_flows_through_outbox_and_worker_until_Finalizado()
    {
        var (id, created) = await CreateOrderAsync();

        Assert.Equal("Pendente", created.GetProperty("status").GetString());

        var final = await Eventually.ReturnsAsync(
            () => GetOrderAsync(id),
            o => o.GetProperty("status").GetString() == "Finalizado",
            because: "O worker deveria finalizar o pedido.");

        var history = final.GetProperty("historico").EnumerateArray()
            .Select(h => h.GetProperty("status_novo").GetString()!)
            .ToArray();
        Assert.Equal(["Pendente", "Processando", "Finalizado"], history);
        Assert.NotEqual(JsonValueKind.Null, final.GetProperty("finalizado_em").ValueKind);
    }

    [Fact]
    public async Task Finalizado_only_happens_after_the_configured_processing_delay()
    {
        var (id, _) = await CreateOrderAsync();
        var final = await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        var started = final.GetProperty("processamento_iniciado_em").GetDateTimeOffset();
        var finished = final.GetProperty("finalizado_em").GetDateTimeOffset();
        Assert.True(finished - started >= TimeSpan.FromSeconds(0.95), $"Processando -> Finalizado levou {finished - started}.");
    }

    [Fact]
    public async Task Published_message_carries_CorrelationId_equal_to_OrderId_and_EventType_OrderCreated()
    {
        // Fila de teste ligada ao mesmo exchange: recebe uma copia da mensagem real publicada pelo outbox.
        var factory = new ConnectionFactory { HostName = env.RabbitHost, Port = env.RabbitPort, UserName = "orders", Password = "orders-test-pw" };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(MessagingTopology.EventsExchange, ExchangeType.Topic, durable: true);
        var probe = await channel.QueueDeclareAsync(queue: "", exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(probe.QueueName, MessagingTopology.EventsExchange, MessagingTopology.OrderCreatedRoutingKey);

        var (id, _) = await CreateOrderAsync();

        BasicGetResult? message = null;
        await Eventually.ReturnsAsync(async () =>
        {
            message = await channel.BasicGetAsync(probe.QueueName, autoAck: true);
            return message is not null && message.BasicProperties.CorrelationId == id.ToString();
        }, found => found, because: "Mensagem OrderCreated nao chegou ao broker.");

        var props = message!.BasicProperties;
        Assert.Equal(id.ToString(), props.CorrelationId);
        Assert.Equal("OrderCreated", props.Type);
        Assert.Equal(DeliveryModes.Persistent, props.DeliveryMode);
        Assert.Equal(id.ToString(), Encoding.UTF8.GetString((byte[])props.Headers!["CorrelationId"]!));
        Assert.Equal("OrderCreated", Encoding.UTF8.GetString((byte[])props.Headers["EventType"]!));

        var envelope = JsonSerializer.Deserialize<IntegrationEvent>(message.Body.Span, MessagingJson.Options)!;
        Assert.Equal(id, envelope.CorrelationId);
        Assert.Equal("OrderCreated", envelope.EventType);
        Assert.Equal(id, envelope.GetData<OrderCreatedData>().OrderId);
    }

    [Fact]
    public async Task Duplicate_delivery_of_the_same_message_is_ignored_by_the_idempotent_consumer()
    {
        var (id, _) = await CreateOrderAsync();
        await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        // Reentrega: mesma mensagem (mesmo MessageId) publicada novamente, como faz um broker "at-least-once".
        await using var db = env.CreateDbContext();
        var original = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == id && m.EventType == "OrderCreated");
        await PublishRawAsync(original.Payload, original.Id, id, "OrderCreated", MessagingTopology.OrderCreatedRoutingKey);
        await PublishRawAsync(original.Payload, original.Id, id, "OrderCreated", MessagingTopology.OrderCreatedRoutingKey);
        await WaitForQueueDrainedAsync();

        var historyCount = await db.OrderStatusHistory.CountAsync(h => h.OrderId == id);
        var inboxCount = await db.InboxMessages.CountAsync(m => m.MessageId == original.Id);
        Assert.Equal(3, historyCount);
        Assert.Equal(1, inboxCount);
    }

    [Fact]
    public async Task A_different_message_for_an_already_finalized_order_does_not_change_anything()
    {
        var (id, _) = await CreateOrderAsync();
        await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        var newMessageId = Guid.NewGuid();
        var payload = EnvelopeJson(newMessageId, id);
        await PublishRawAsync(payload, newMessageId, id, "OrderCreated", MessagingTopology.OrderCreatedRoutingKey);
        await WaitForQueueDrainedAsync();

        await using var db = env.CreateDbContext();
        Assert.Equal(3, await db.OrderStatusHistory.CountAsync(h => h.OrderId == id));
        Assert.Equal(OrderStatus.Finalizado, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id)).Status);
    }

    [Fact]
    public async Task Worker_resumes_an_order_left_in_Processando_after_a_crash_without_repeating_the_transition()
    {
        // Simula um worker que caiu 0,5s depois de marcar "Processando": o estado ja esta no banco.
        var id = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMilliseconds(-500);
        await using (var db = env.CreateDbContext())
        {
            var order = Order.Create("Cliente Crash", "Produto", 99m, startedAt.AddMilliseconds(-100));
            order.StartProcessing(startedAt);
            await db.Orders.AddAsync(order);
            await db.SaveChangesAsync();
            id = order.Id;
        }

        var messageId = Guid.NewGuid();
        await PublishRawAsync(EnvelopeJson(messageId, id), messageId, id, "OrderCreated", MessagingTopology.OrderCreatedRoutingKey);

        var final = await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        var statuses = final.GetProperty("historico").EnumerateArray().Select(h => h.GetProperty("status_novo").GetString()!).ToArray();
        Assert.Equal(["Pendente", "Processando", "Finalizado"], statuses); // "Processando" aparece uma unica vez
    }

    [Fact]
    public async Task Concurrent_consumers_for_the_same_order_produce_a_single_transition_sequence()
    {
        var (id, _) = await CreateOrderAsync();

        // Varias mensagens distintas (MessageIds diferentes) para o MESMO pedido, em paralelo.
        for (var i = 0; i < 5; i++)
        {
            var messageId = Guid.NewGuid();
            await PublishRawAsync(EnvelopeJson(messageId, id), messageId, id, "OrderCreated", MessagingTopology.OrderCreatedRoutingKey);
        }

        await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");
        await WaitForQueueDrainedAsync();

        await using var db = env.CreateDbContext();
        var history = await db.OrderStatusHistory.AsNoTracking().Where(h => h.OrderId == id).OrderBy(h => h.AlteradoEm).Select(h => h.StatusNovo).ToListAsync();
        Assert.Equal([OrderStatus.Pendente, OrderStatus.Processando, OrderStatus.Finalizado], history);
    }

    [Fact]
    public async Task Status_changes_are_published_as_OrderStatusChanged_events_through_the_outbox()
    {
        var (id, _) = await CreateOrderAsync();
        await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        await using var db = env.CreateDbContext();
        var events = await Eventually.ReturnsAsync(
            async () => await db.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == id && m.EventType == "OrderStatusChanged" && m.ProcessedAt != null).CountAsync(),
            count => count == 2,
            because: "Esperados 2 eventos OrderStatusChanged (Processando e Finalizado) publicados.");
        Assert.Equal(2, events);
    }

    [Fact]
    public async Task Idempotency_key_prevents_duplicate_orders()
    {
        var key = Guid.NewGuid().ToString();
        var (first, _) = await CreateOrderAsync(idempotencyKey: key);
        var (second, _) = await CreateOrderAsync(idempotencyKey: key);

        Assert.Equal(first, second);
        await using var db = env.CreateDbContext();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.IdempotencyKey == key));
    }

    [Fact]
    public async Task Order_creation_stores_order_history_and_outbox_message_atomically()
    {
        var (id, _) = await CreateOrderAsync();

        await using var db = env.CreateDbContext();
        Assert.True(await db.Orders.AnyAsync(o => o.Id == id));
        Assert.True(await db.OrderStatusHistory.AnyAsync(h => h.OrderId == id && h.StatusAnterior == null && h.StatusNovo == OrderStatus.Pendente));
        Assert.True(await db.OutboxMessages.AnyAsync(m => m.CorrelationId == id && m.EventType == "OrderCreated"));
    }

    [Theory]
    [InlineData("", "Produto", 10, "cliente")]
    [InlineData("Cliente", "", 10, "produto")]
    [InlineData("Cliente", "Produto", 0, "valor")]
    [InlineData("Cliente", "Produto", -5, "valor")]
    [InlineData("Cliente", "Produto", 10.999, "valor")]
    public async Task Invalid_orders_are_rejected_with_400_and_field_errors(string cliente, string produto, double valor, string field)
    {
        var response = await Client.PostAsJsonAsync("/orders", new { cliente, produto, valor });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"Esperado erro no campo '{field}'.");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("nao e json")]
    public async Task Malformed_or_missing_json_body_is_a_400_not_a_500(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Extreme_page_numbers_are_rejected_instead_of_overflowing()
    {
        var response = await Client.GetAsync("/orders?page=2147483647&page_size=200");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404_problem_details()
    {
        var response = await Client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task List_orders_supports_status_filter_and_pagination()
    {
        var (id, _) = await CreateOrderAsync("Cliente Lista");
        await Eventually.ReturnsAsync(() => GetOrderAsync(id), o => o.GetProperty("status").GetString() == "Finalizado");

        var page = await Client.GetFromJsonAsync<JsonElement>("/orders?status=Finalizado&page=1&page_size=2");

        Assert.Equal(2, page.GetProperty("page_size").GetInt32());
        Assert.True(page.GetProperty("items").GetArrayLength() <= 2);
        Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.Equal("Finalizado", item.GetProperty("status").GetString()));

        var invalid = await Client.GetAsync("/orders?page_size=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Api_uses_snake_case_field_names_from_the_challenge()
    {
        var (_, body) = await CreateOrderAsync();

        foreach (var field in new[] { "id", "cliente", "produto", "valor", "status", "data_criacao" })
        {
            Assert.True(body.TryGetProperty(field, out _), $"Campo '{field}' ausente.");
        }
    }

    [Fact]
    public async Task Health_ready_reports_database_and_messaging_healthy()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", health.GetProperty("status").GetString());
        var entries = health.GetProperty("entries");
        Assert.Equal("Healthy", entries.GetProperty("postgresql").GetProperty("status").GetString());
        Assert.Equal("Healthy", entries.GetProperty("rabbitmq").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health/live")).StatusCode);
    }

    private static string EnvelopeJson(Guid messageId, Guid orderId) =>
        JsonSerializer.Serialize(
            new IntegrationEvent(
                messageId,
                EventTypes.OrderCreated,
                orderId,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(new OrderCreatedData(orderId, "c", "p", 1m, DateTimeOffset.UtcNow), MessagingJson.Options)),
            MessagingJson.Options);

    private async Task PublishRawAsync(string payload, Guid messageId, Guid correlationId, string type, string routingKey)
    {
        var factory = new ConnectionFactory { HostName = env.RabbitHost, Port = env.RabbitPort, UserName = "orders", Password = "orders-test-pw" };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var props = new BasicProperties
        {
            MessageId = messageId.ToString(),
            CorrelationId = correlationId.ToString(),
            Type = type,
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
        };
        await channel.BasicPublishAsync(MessagingTopology.EventsExchange, routingKey, mandatory: false, props, Encoding.UTF8.GetBytes(payload));
    }

    private async Task WaitForQueueDrainedAsync()
    {
        var factory = new ConnectionFactory { HostName = env.RabbitHost, Port = env.RabbitPort, UserName = "orders", Password = "orders-test-pw" };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await Eventually.ReturnsAsync(
            async () =>
            {
                var q = await channel.QueueDeclarePassiveAsync(MessagingTopology.OrderCreatedQueue);
                return q.MessageCount;
            },
            count => count == 0,
            because: "A fila deveria esvaziar.");
        await Task.Delay(1500); // margem para o ultimo ack/commit em andamento (prefetch)
    }
}

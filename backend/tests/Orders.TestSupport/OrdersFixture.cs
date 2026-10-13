using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;
using Orders.Worker;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Orders.TestSupport;

/// <summary>
/// Ambiente de teste real e descartavel: PostgreSQL + RabbitMQ em containers (Testcontainers),
/// a API completa (WebApplicationFactory) e o Worker rodando em memoria no mesmo processo.
/// Nada e mockado: o fluxo POST -> Outbox -> RabbitMQ -> Worker -> Finalizado e o de producao.
/// </summary>
public sealed class OrdersFixture : IAsyncLifetime
{
    public const string ReadOnlyUser = "orders_readonly";
    public const string ReadOnlyPassword = "readonly-test-pw";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orders")
        .WithUsername("orders")
        .WithPassword("orders-test-pw")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("orders")
        .WithPassword("orders-test-pw")
        .Build();

    private WebApplicationFactory<Orders.Api.ApiAssemblyMarker>? _api;
    private IHost? _worker;

    public string ConnectionString => _postgres.GetConnectionString();

    public string ReadOnlyConnectionString =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Username = ReadOnlyUser, Password = ReadOnlyPassword }.ConnectionString;

    public string RabbitHost => _rabbit.Hostname;

    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public WebApplicationFactory<Orders.Api.ApiAssemblyMarker> Api => _api ?? throw new InvalidOperationException("Fixture nao inicializada.");

    public IServiceProvider WorkerServices => _worker?.Services ?? throw new InvalidOperationException("Fixture nao inicializada.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        // Migrations reais + usuario somente leitura, exatamente como o servico "migrator" faz em producao.
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE {ReadOnlyUser} LOGIN PASSWORD '{ReadOnlyPassword}';
                GRANT CONNECT ON DATABASE orders TO {ReadOnlyUser};
                GRANT USAGE ON SCHEMA public TO {ReadOnlyUser};
                GRANT SELECT ON vw_pedidos, vw_historico_status TO {ReadOnlyUser};
                ALTER ROLE {ReadOnlyUser} SET default_transaction_read_only = on;
                """);
        }

        // WebApplicationFactory le a configuracao no startup: variaveis de ambiente sao o caminho mais confiavel.
        SetEnvironment();
        _api = new WebApplicationFactory<Orders.Api.ApiAssemblyMarker>();
        _ = _api.Server; // forca o startup (e o relay do outbox)

        _worker = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                Orders.Application.ApplicationServiceCollectionExtensions.AddApplication(services, context.Configuration);
                services.AddPersistence(context.Configuration);
                services.AddMessaging(context.Configuration);
                services.AddHostedService<OrderCreatedConsumer>();
            })
            .Build();
        await _worker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync();
            _worker.Dispose();
        }

        _api?.Dispose();
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public OrdersDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<OrdersDbContext>();
        builder.UseOrdersDatabase(ConnectionString);
        return new OrdersDbContext(builder.Options);
    }

    private void SetEnvironment()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__OrdersDb", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__OrdersDbReadOnly", ReadOnlyConnectionString);
        Environment.SetEnvironmentVariable("RabbitMq__Host", RabbitHost);
        Environment.SetEnvironmentVariable("RabbitMq__Port", RabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("RabbitMq__Username", "orders");
        Environment.SetEnvironmentVariable("RabbitMq__Password", "orders-test-pw");
        // Mantem o teste rapido sem alterar a regra: o desafio usa 5s em producao.
        Environment.SetEnvironmentVariable("Processing__DelaySeconds", "1");
        Environment.SetEnvironmentVariable("Outbox__PollingIntervalMs", "100");
    }
}

public static class OrdersCollectionNames
{
    /// <summary>Nome da collection xUnit. A definicao (CollectionDefinition) precisa existir em cada assembly de teste.</summary>
    public const string Environment = "orders-environment";
}

using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Npgsql;
using Orders.Application.Ai;
using Orders.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Messaging;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "OrdersDb";
    public const string ReadyTag = "ready";
    public const string ReadOnlyConnectionStringName = "OrdersDbReadOnly";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' nao configurada (variavel ConnectionStrings__{ConnectionStringName}).");

        services.AddSingleton<OutboxSignal>();
        services.AddDbContext<OrdersDbContext>(o => o.UseOrdersDatabase(connectionString));
        services.AddScoped<IOrdersDbContext>(sp => sp.GetRequiredService<OrdersDbContext>());
        services.AddScoped<IEventOutbox, EfEventOutbox>();
        services.AddScoped<IInbox, EfInbox>();

        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgresql", tags: [ReadyTag]);

        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddHostedService<OutboxRelayService>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag]);

        return services;
    }

    /// <summary>
    /// Modulo de IA: so e ativado com ApiKey + connection string do usuario somente leitura.
    /// </summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        var readOnlyConnection = configuration.GetConnectionString(ReadOnlyConnectionStringName);

        if (string.IsNullOrWhiteSpace(ai.ApiKey) || string.IsNullOrWhiteSpace(readOnlyConnection))
        {
            services.AddSingleton<IOrderQuestionAnswerer, DisabledOrderQuestionAnswerer>();
            return services;
        }

        services.AddNpgsqlDataSource(readOnlyConnection, serviceKey: ReadOnlyConnectionStringName);
        services.AddSingleton(sp => new AnalyticsQueries(sp.GetRequiredKeyedService<NpgsqlDataSource>(ReadOnlyConnectionStringName)));
        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return new ChatClientBuilder(new AnthropicClient { ApiKey = options.ApiKey }.AsIChatClient(options.Model, options.MaxOutputTokens))
                .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = options.MaxToolIterations)
                .Build(sp);
        });
        services.AddSingleton<IOrderQuestionAnswerer, OrdersAssistant>();
        return services;
    }

    public static DbContextOptionsBuilder UseOrdersDatabase(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}

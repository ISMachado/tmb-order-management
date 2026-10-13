using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Orders.Application;
using Orders.Infrastructure;
using Orders.Infrastructure.Observability;
using Orders.Worker;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, serviceName: "orders-worker");
builder.Services.AddHostedService<OrderCreatedConsumer>();

var app = builder.Build();

// O worker nao expoe API de negocio; apenas health checks (banco + RabbitMQ) para o orquestrador.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadyTag) });

await app.RunAsync();

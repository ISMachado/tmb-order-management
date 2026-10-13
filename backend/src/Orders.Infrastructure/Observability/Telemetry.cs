using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Orders.Infrastructure.Observability;

public static class Telemetry
{
    public const string SourceName = "Orders.Messaging";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Tracing ponta a ponta com OpenTelemetry: requisicoes HTTP, consultas no PostgreSQL e
    /// publicacao/consumo de mensagens. Exporta via OTLP (ex.: Jaeger) quando OTEL_EXPORTER_OTLP_ENDPOINT esta definido.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(SourceName)
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsNoise(ctx.Request.Path))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }

    private static bool IsNoise(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/hubs");
}

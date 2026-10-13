using System.Text.Json;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Endpoints;
using Orders.Api.Realtime;
using Orders.Application;
using Orders.Infrastructure;
using Orders.Infrastructure.Observability;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

// JSON em snake_case: id, cliente, produto, valor, status, data_criacao (nomes literais do desafio).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddAi(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, serviceName: "orders-api");
builder.Services.AddHostedService<StatusChangedListener>();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["trace_id"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
});

builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();

// Cada chamada a IA custa dinheiro: limita por IP (10 perguntas por minuto).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AiEndpoints.RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Location")));

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var problem = context.RequestServices.GetRequiredService<IProblemDetailsService>();

    // Corpo ausente ou JSON malformado e erro do CLIENTE (400), nao falha do servidor.
    if (exception is BadHttpRequestException badRequest)
    {
        context.Response.StatusCode = badRequest.StatusCode;
        await problem.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = badRequest.StatusCode, Title = "Requisicao invalida", Detail = "O corpo da requisicao esta ausente ou nao e um JSON valido." },
        });
        return;
    }

    Log.Error(exception, "Erro nao tratado em {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await problem.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = new ProblemDetails { Status = 500, Title = "Erro interno", Detail = "Ocorreu um erro inesperado. Informe o trace_id ao suporte." },
    });
}));
app.UseStatusCodePages();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);
app.UseCors();
app.UseRateLimiter();

app.MapOrderEndpoints();
app.MapAiEndpoints();
app.MapHub<OrdersHub>("/hubs/orders");

// Health checks: /health/live = processo de pe; /health/ready = API + PostgreSQL + RabbitMQ (JSON detalhado).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadyTag),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("API de Pedidos"));
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

await app.RunAsync();

namespace Orders.Api
{
    /// <summary>Marcador publico da assembly da API, usado como ponto de entrada pelo WebApplicationFactory nos testes.</summary>
    public sealed class ApiAssemblyMarker;
}

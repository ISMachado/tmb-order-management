using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Orders.Application.Ai;

namespace Orders.Api.Endpoints;

public static class AiEndpoints
{
    public const string RateLimitPolicy = "ai";
    private const int MaxQuestionLength = 500;

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai").WithTags("IA - Pergunte sobre os pedidos");

        group.MapGet("/status", (IOrderQuestionAnswerer answerer) => TypedResults.Ok(new AiStatusResponse(answerer.IsEnabled, answerer.Model)))
            .WithName("AiStatus")
            .WithSummary("Informa se o modulo de IA esta habilitado");

        group.MapPost("/ask", AskAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("AskAboutOrders")
            .WithSummary("Pergunta em linguagem natural sobre os pedidos")
            .WithDescription("A LLM interpreta a pergunta, consulta o banco (somente leitura) por ferramentas controladas e responde com dados reais.");

        return app;
    }

    private static async Task<Results<Ok<AskQuestionResponse>, ValidationProblem, ProblemHttpResult>> AskAsync(
        AskQuestionRequest request,
        IOrderQuestionAnswerer answerer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Pergunta))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["pergunta"] = ["Informe uma pergunta."] });
        }

        if (request.Pergunta.Length > MaxQuestionLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["pergunta"] = [$"A pergunta deve ter no maximo {MaxQuestionLength} caracteres."] });
        }

        try
        {
            return TypedResults.Ok(await answerer.AskAsync(request.Pergunta, ct));
        }
        catch (AiDisabledException ex)
        {
            return TypedResults.Problem(title: "Modulo de IA desativado", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AiProviderException ex)
        {
            return TypedResults.Problem(title: "Provedor de IA indisponivel", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

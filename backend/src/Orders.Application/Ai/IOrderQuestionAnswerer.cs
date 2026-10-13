namespace Orders.Application.Ai;

public sealed record AskQuestionRequest(string Pergunta);

/// <summary>Uma consulta executada pelo assistente para chegar na resposta (transparencia para o usuario).</summary>
public sealed record ExecutedQuery(string Ferramenta, string Parametros, string? Sql);

public sealed record AskQuestionResponse(string Resposta, IReadOnlyList<ExecutedQuery> ConsultasExecutadas);

public sealed record AiStatusResponse(bool Habilitado, string? Modelo);

/// <summary>Responde perguntas em linguagem natural sobre os pedidos usando uma LLM e dados reais do banco.</summary>
public interface IOrderQuestionAnswerer
{
    bool IsEnabled { get; }

    string? Model { get; }

    Task<AskQuestionResponse> AskAsync(string question, CancellationToken ct);
}

/// <summary>A IA nao esta configurada (sem chave de API).</summary>
public sealed class AiDisabledException() : Exception("O modulo de IA esta desativado. Configure ANTHROPIC_API_KEY no arquivo .env.");

/// <summary>Falha ao falar com o provedor de LLM.</summary>
public sealed class AiProviderException(string message, Exception inner) : Exception(message, inner);

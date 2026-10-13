using System.Text.RegularExpressions;

namespace Orders.Infrastructure.Ai;

/// <summary>
/// Primeira camada de defesa da ferramenta de SQL livre. As camadas seguintes (que realmente garantem a seguranca) sao:
/// usuario do banco somente leitura com acesso apenas as views, transacao READ ONLY, statement_timeout e LIMIT forcado.
/// </summary>
public static partial class SqlGuard
{
    public const int MaxRows = 100;

    private static readonly HashSet<string> AllowedRelations = new(StringComparer.OrdinalIgnoreCase)
    {
        "vw_pedidos",
        "vw_historico_status",
    };

    public static bool TryValidate(string? sql, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(sql))
        {
            error = "A consulta esta vazia.";
            return false;
        }

        var text = sql.Trim().TrimEnd(';').Trim();

        if (text.Contains(';', StringComparison.Ordinal))
        {
            error = "Apenas um comando por consulta e permitido.";
            return false;
        }

        if (text.Contains("--", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
        {
            error = "Comentarios SQL nao sao permitidos.";
            return false;
        }

        if (!StartsWithSelectRegex().IsMatch(text))
        {
            error = "Somente consultas SELECT (ou WITH ... SELECT) sao permitidas.";
            return false;
        }

        if (ForbiddenRegex().Match(text) is { Success: true } forbidden)
        {
            error = $"Palavra nao permitida na consulta: '{forbidden.Value}'.";
            return false;
        }

        // "FROM" dentro de funcoes (EXTRACT(EPOCH FROM col), SUBSTRING(x FROM 1)) nao e uma relacao.
        var withoutFunctionFrom = FunctionWithFromRegex().Replace(text, "(expr)");

        foreach (Match relation in RelationRegex().Matches(withoutFunctionFrom))
        {
            var name = relation.Groups["name"].Value.Trim('"');
            if (!AllowedRelations.Contains(name) && !IsCteName(text, name))
            {
                error = $"Relacao nao permitida: '{name}'. Use apenas: {string.Join(", ", AllowedRelations)}.";
                return false;
            }
        }

        normalized = text;
        return true;
    }

    /// <summary>Envolve a consulta limitando o numero de linhas devolvidas.</summary>
    public static string WithRowLimit(string validatedSql) => $"SELECT * FROM ({validatedSql}) AS resultado LIMIT {MaxRows}";

    private static bool IsCteName(string sql, string name) =>
        Regex.IsMatch(sql, $@"\b{Regex.Escape(name)}\s+AS\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    [GeneratedRegex(@"^\s*(select|with)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex StartsWithSelectRegex();

    [GeneratedRegex(
        @"\b(insert|update|delete|drop|alter|create|truncate|grant|revoke|copy|call|do|execute|prepare|set|reset|vacuum|analyze|lock|listen|notify|merge|into|pg_[a-z_]*|information_schema|current_setting|set_config|lo_[a-z_]*|dblink)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ForbiddenRegex();

    [GeneratedRegex(@"\b(extract|substring|trim|overlay|position)\s*\((?:[^()]|\([^()]*\))*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex FunctionWithFromRegex();

    [GeneratedRegex(@"\b(?:from|join)\s+(?<name>""?[a-zA-Z_][a-zA-Z0-9_\.]*""?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex RelationRegex();
}

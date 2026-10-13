using System.Data;
using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Orders.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Chave da API do provedor de LLM. Vazia = modulo de IA desativado.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-haiku-5-5";

    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Limite de idas e voltas entre a LLM e as ferramentas por pergunta.</summary>
    public int MaxToolIterations { get; set; } = 6;

    public string TimeZoneId { get; set; } = "America/Sao_Paulo";
}

/// <summary>Filtro comum das consultas analiticas. Datas ja convertidas para UTC (inicio inclusivo, fim exclusivo).</summary>
public sealed record OrderFilter(string? Status, DateTimeOffset? From, DateTimeOffset? To, string DateField = "criacao", string? Cliente = null);

/// <summary>
/// Consultas analiticas SEMPRE parametrizadas e executadas com o usuario somente leitura (so enxerga as views).
/// Os valores vindos da LLM nunca sao concatenados no SQL: apenas o nome da coluna de data, escolhido de uma lista fixa.
/// </summary>
public sealed class AnalyticsQueries(NpgsqlDataSource dataSource)
{
    public static readonly string[] ValidStatuses = ["Pendente", "Processando", "Finalizado"];

    public async Task<(long Total, string Sql)> CountAsync(OrderFilter filter, CancellationToken ct)
    {
        var (where, parameters) = BuildWhere(filter);
        var sql = $"SELECT COUNT(*) FROM vw_pedidos{where}";
        var value = await ScalarAsync(sql, parameters, ct);
        return (Convert.ToInt64(value, CultureInfo.InvariantCulture), sql);
    }

    public async Task<(decimal Total, long Pedidos, string Sql)> SumAsync(OrderFilter filter, CancellationToken ct)
    {
        var (where, parameters) = BuildWhere(filter);
        var sql = $"SELECT COALESCE(SUM(valor), 0), COUNT(*) FROM vw_pedidos{where}";
        var rows = await QueryAsync(sql, parameters, 1, ct);
        return (Convert.ToDecimal(rows[0]["coalesce"], CultureInfo.InvariantCulture), Convert.ToInt64(rows[0]["count"], CultureInfo.InvariantCulture), sql);
    }

    public async Task<(IReadOnlyDictionary<string, object?> Stats, string Sql)> ProcessingTimeAsync(OrderFilter filter, CancellationToken ct)
    {
        // So pedidos finalizados tem tempo de processamento.
        var finalized = filter with { Status = "Finalizado" };
        var (where, parameters) = BuildWhere(finalized);
        var sql = $"""
            SELECT COUNT(*) AS pedidos_finalizados,
                   AVG(segundos_ate_finalizar) AS media_segundos,
                   MIN(segundos_ate_finalizar) AS minimo_segundos,
                   MAX(segundos_ate_finalizar) AS maximo_segundos
            FROM vw_pedidos{where}
            """;
        var rows = await QueryAsync(sql, parameters, 1, ct);
        return (rows[0], sql);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string Sql)> ListAsync(OrderFilter filter, int limit, CancellationToken ct)
    {
        var (where, parameters) = BuildWhere(filter);
        var sql = $"""
            SELECT id, cliente, produto, valor, status, data_criacao, finalizado_em
            FROM vw_pedidos{where}
            ORDER BY data_criacao DESC
            LIMIT {Math.Clamp(limit, 1, 20)}
            """;
        return (await QueryAsync(sql, parameters, 20, ct), sql);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string Sql)> GroupByStatusAsync(OrderFilter filter, CancellationToken ct)
    {
        var (where, parameters) = BuildWhere(filter with { Status = null });
        var sql = $"SELECT status, COUNT(*) AS pedidos, COALESCE(SUM(valor), 0) AS valor_total FROM vw_pedidos{where} GROUP BY status ORDER BY status";
        return (await QueryAsync(sql, parameters, 10, ct), sql);
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string Sql)> TopAsync(string dimension, OrderFilter filter, int limit, CancellationToken ct)
    {
        // "dimension" vem de uma lista fixa (cliente|produto), nunca do texto da LLM.
        var column = dimension == "produto" ? "produto" : "cliente";
        var (where, parameters) = BuildWhere(filter);
        var sql = $"""
            SELECT {column}, COUNT(*) AS pedidos, COALESCE(SUM(valor), 0) AS valor_total
            FROM vw_pedidos{where}
            GROUP BY {column}
            ORDER BY valor_total DESC
            LIMIT {Math.Clamp(limit, 1, 20)}
            """;
        return (await QueryAsync(sql, parameters, 20, ct), sql);
    }

    /// <summary>Ferramenta de reserva: SQL escrito pela LLM, validado por <see cref="SqlGuard"/> e executado em transacao READ ONLY.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RunReadOnlyAsync(string validatedSql, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var guard = new NpgsqlCommand("SET LOCAL transaction_read_only = on; SET LOCAL statement_timeout = '3s';", connection, transaction))
        {
            await guard.ExecuteNonQueryAsync(ct);
        }

        await using var command = new NpgsqlCommand(SqlGuard.WithRowLimit(validatedSql), connection, transaction);
        var rows = await ReadAllAsync(command, SqlGuard.MaxRows, ct);
        await transaction.RollbackAsync(ct);
        return rows;
    }

    private static (string Where, List<NpgsqlParameter> Parameters) BuildWhere(OrderFilter f)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        var dateColumn = f.DateField == "finalizacao" ? "finalizado_em" : "data_criacao";

        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            conditions.Add("status = @status");
            parameters.Add(new NpgsqlParameter("status", f.Status));
        }

        if (f.From is { } from)
        {
            conditions.Add($"{dateColumn} >= @from");
            parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.UtcDateTime });
        }

        if (f.To is { } to)
        {
            conditions.Add($"{dateColumn} < @to");
            parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.UtcDateTime });
        }

        if (!string.IsNullOrWhiteSpace(f.Cliente))
        {
            conditions.Add("cliente ILIKE @cliente");
            parameters.Add(new NpgsqlParameter("cliente", "%" + f.Cliente.Replace("%", "\\%", StringComparison.Ordinal) + "%"));
        }

        return (conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions), parameters);
    }

    private async Task<object?> ScalarAsync(string sql, List<NpgsqlParameter> parameters, CancellationToken ct)
    {
        var rows = await QueryAsync(sql, parameters, 1, ct);
        return rows[0].Values.First();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(string sql, List<NpgsqlParameter> parameters, int maxRows, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        return await ReadAllAsync(command, maxRows, ct);
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAllAsync(NpgsqlCommand command, int maxRows, CancellationToken ct)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (rows.Count < maxRows && await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Normalize(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object Normalize(object value) => value switch
    {
        DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        decimal d => Math.Round(d, 3),
        _ => value,
    };
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orders.Infrastructure.Persistence;

namespace Orders.Migrator;

/// <summary>
/// Cria/atualiza o usuario do PostgreSQL usado pelo modulo de IA. Ele so enxerga as views analiticas,
/// toda transacao dele e somente leitura e cada consulta tem limite de tempo. Mesmo que a LLM gere uma
/// consulta maliciosa, o banco recusa qualquer escrita.
/// </summary>
internal static class ReadOnlyRoleProvisioner
{
    public static async Task EnsureAsync(OrdersDbContext db, IConfiguration config, ILogger logger)
    {
        var connectionString = config["ConnectionStrings:OrdersDbReadOnly"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogInformation("ConnectionStrings__OrdersDbReadOnly nao definida; usuario somente leitura nao sera provisionado.");
            return;
        }

        var readOnly = new NpgsqlConnectionStringBuilder(connectionString);
        var role = readOnly.Username ?? throw new InvalidOperationException("Usuario ausente em OrdersDbReadOnly.");
        var password = readOnly.Password ?? throw new InvalidOperationException("Senha ausente em OrdersDbReadOnly.");
        var database = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;

        var roleIdent = QuoteIdent(role);
        var passwordLiteral = "'" + password.Replace("'", "''", StringComparison.Ordinal) + "'";
        var roleLiteral = "'" + role.Replace("'", "''", StringComparison.Ordinal) + "'";

        // DDL nao aceita parametros; os valores vem de variaveis de ambiente controladas por nos e sao escapados.
        var sql = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = {roleLiteral}) THEN
                    CREATE ROLE {roleIdent} LOGIN PASSWORD {passwordLiteral};
                ELSE
                    ALTER ROLE {roleIdent} WITH LOGIN PASSWORD {passwordLiteral};
                END IF;
            END
            $$;
            GRANT CONNECT ON DATABASE {QuoteIdent(database!)} TO {roleIdent};
            GRANT USAGE ON SCHEMA public TO {roleIdent};
            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {roleIdent};
            GRANT SELECT ON vw_pedidos, vw_historico_status TO {roleIdent};
            ALTER ROLE {roleIdent} SET default_transaction_read_only = on;
            ALTER ROLE {roleIdent} SET statement_timeout = '5s';
            """;

#pragma warning disable EF1002 // Valores escapados acima; DDL do PostgreSQL nao suporta parametros.
        await db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
        logger.LogInformation("Usuario somente leitura '{Role}' provisionado para o modulo de IA.", role);
    }

    private static string QuoteIdent(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Views usadas pelo modulo de IA (usuario somente leitura). Colunas em portugues e valores textuais,
            // para a LLM interpretar sem ambiguidade. O usuario de IA nao tem acesso as tabelas base.
            migrationBuilder.Sql("""
                CREATE VIEW vw_pedidos AS
                SELECT
                    id,
                    cliente,
                    produto,
                    valor,
                    status,
                    data_criacao,
                    data_atualizacao,
                    processamento_iniciado_em,
                    finalizado_em,
                    EXTRACT(EPOCH FROM (finalizado_em - data_criacao))::numeric(14,3) AS segundos_ate_finalizar
                FROM orders;
                """);

            migrationBuilder.Sql("""
                CREATE VIEW vw_historico_status AS
                SELECT order_id AS pedido_id, status_anterior, status_novo, alterado_em
                FROM order_status_history;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS vw_historico_status;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS vw_pedidos;");
        }
    }
}

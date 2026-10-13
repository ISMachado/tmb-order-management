using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orders.Infrastructure.Messaging;

/// <summary>Verifica se ha conexao aberta com o RabbitMQ (abre uma, se ainda nao existir).</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connectionProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy($"Conectado a {connection.Endpoint}.")
                : HealthCheckResult.Unhealthy("Conexao com o RabbitMQ fechada (recuperacao automatica em andamento).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Nao foi possivel conectar ao RabbitMQ.", ex);
        }
    }
}

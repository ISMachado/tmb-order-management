using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Orders.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Quantas mensagens o consumidor processa em paralelo (prefetch + concorrencia).</summary>
    public ushort PrefetchCount { get; set; } = 10;
}

/// <summary>
/// Mantem UMA conexao AMQP por processo (recomendacao do RabbitMQ) com recuperacao automatica.
/// Canais sao criados por quem precisa (publisher, consumidores).
/// </summary>
public sealed class RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection?.IsOpen == true;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Port,
                UserName = o.Username,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                ConsumerDispatchConcurrency = o.PrefetchCount,
            };

            _connection = await factory.CreateConnectionAsync(ct);
            logger.LogInformation("Conectado ao RabbitMQ em {Host}:{Port}.", o.Host, o.Port);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _lock.Dispose();
    }
}

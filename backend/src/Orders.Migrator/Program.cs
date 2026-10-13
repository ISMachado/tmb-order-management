using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;
using Orders.Migrator;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// Servico de execucao unica: aplica as migrations, provisiona o usuario somente leitura do modulo de IA,
// opcionalmente popula dados de demonstracao e encerra. API e Worker so sobem depois que ele termina com sucesso.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] migrator: {Message:lj}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(TimeProvider.System);
    var connectionString = builder.Configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName)
        ?? throw new InvalidOperationException("ConnectionStrings__OrdersDb nao configurada.");
    builder.Services.AddDbContext<OrdersDbContext>(o => o.UseOrdersDatabase(connectionString));

    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Orders.Migrator");

    await MigrateWithRetryAsync(db, logger);
    await ReadOnlyRoleProvisioner.EnsureAsync(db, config, logger);

    if (config.GetValue<bool>("Seed:Enabled"))
    {
        await DemoDataSeeder.SeedAsync(db, TimeProvider.System, logger);
    }

    Log.Information("Banco pronto.");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Falha ao preparar o banco de dados.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task MigrateWithRetryAsync(OrdersDbContext db, ILogger logger)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            logger.LogInformation("Migrations pendentes: {Count} {Names}", pending.Count, pending);
            await db.Database.MigrateAsync();
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning(ex, "Banco indisponivel (tentativa {Attempt}/10); aguardando 3s.", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

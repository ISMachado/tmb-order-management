using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;

namespace Orders.Migrator;

/// <summary>Usado apenas pela ferramenta `dotnet ef` para gerar migrations (nao conecta no banco).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OrdersDb")
            ?? "Host=localhost;Database=orders;Username=orders;Password=design-time-only";
        var builder = new DbContextOptionsBuilder<OrdersDbContext>();
        builder.UseOrdersDatabase(connectionString);
        return new OrdersDbContext(builder.Options);
    }
}

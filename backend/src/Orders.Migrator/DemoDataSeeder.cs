using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orders.Domain;
using Orders.Infrastructure.Persistence;

namespace Orders.Migrator;

/// <summary>
/// Gera pedidos historicos (ja Finalizados) para a demonstracao e para o modulo de IA ter dados para responder.
/// So roda com a tabela vazia e com Seed__Enabled=true.
/// </summary>
internal static class DemoDataSeeder
{
    private static readonly string[] Clientes =
    [
        "Ana Souza", "Bruno Lima", "Carla Mendes", "Diego Alves", "Eduarda Rocha",
        "Felipe Costa", "Gabriela Nunes", "Henrique Dias", "Isabela Martins", "Joao Pereira",
    ];

    private static readonly (string Nome, decimal Preco)[] Produtos =
    [
        ("Notebook Pro 14", 7899.90m), ("Monitor 27\" 4K", 2349.00m), ("Teclado Mecanico", 489.90m),
        ("Mouse sem fio", 159.90m), ("Headset USB-C", 399.00m), ("Cadeira Ergonomica", 1899.00m),
        ("Webcam Full HD", 299.90m), ("SSD NVMe 1TB", 549.90m), ("Dock Station", 899.00m),
        ("Smartphone X", 4299.00m),
    ];

    public static async Task SeedAsync(OrdersDbContext db, TimeProvider time, ILogger logger)
    {
        if (await db.Orders.AnyAsync())
        {
            logger.LogInformation("Seed ignorado: ja existem pedidos.");
            return;
        }

        var random = new Random(42);
        var now = time.GetUtcNow();
        const int count = 60;

        for (var i = 0; i < count; i++)
        {
            // Distribui os pedidos nos ultimos ~45 dias (alguns hoje).
            var daysAgo = i < 5 ? 0 : random.Next(1, 46);
            var createdAt = now.AddDays(-daysAgo).AddMinutes(-random.Next(30, 600));
            var produto = Produtos[random.Next(Produtos.Length)];
            var quantidade = random.Next(1, 4);

            var order = Order.Create(Clientes[random.Next(Clientes.Length)], produto.Nome, produto.Preco * quantidade, createdAt);
            var startedAt = createdAt.AddMilliseconds(random.Next(150, 900));
            order.StartProcessing(startedAt);
            order.Complete(startedAt.AddSeconds(5).AddMilliseconds(random.Next(5, 80)));
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seed: {Count} pedidos de demonstracao criados.", count);
    }
}

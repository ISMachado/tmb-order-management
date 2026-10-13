using Microsoft.EntityFrameworkCore;
using Orders.Domain;

namespace Orders.Application.Abstractions;

/// <summary>
/// Porta de acesso a dados usada pelos casos de uso. A implementacao (EF Core + PostgreSQL)
/// fica na Infrastructure; aqui expomos apenas o necessario para os casos de uso.
/// </summary>
public interface IOrdersDbContext
{
    DbSet<Order> Orders { get; }

    DbSet<OrderStatusHistory> OrderStatusHistory { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarta entidades rastreadas (usado antes de reprocessar apos conflito de concorrencia).</summary>
    void ResetTracking();
}

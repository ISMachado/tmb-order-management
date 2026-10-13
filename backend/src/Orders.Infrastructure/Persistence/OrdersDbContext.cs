using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Domain;
using Orders.Infrastructure.Messaging;

namespace Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, OutboxSignal? outboxSignal = null)
    : DbContext(options), IOrdersDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public void ResetTracking() => ChangeTracker.Clear();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var hasNewOutboxMessages = ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added);
        var result = await base.SaveChangesAsync(cancellationToken);

        // Acorda o relay imediatamente (sem esperar o proximo ciclo de polling) apos o commit.
        if (hasNewOutboxMessages && Database.CurrentTransaction is null)
        {
            outboxSignal?.Notify();
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}

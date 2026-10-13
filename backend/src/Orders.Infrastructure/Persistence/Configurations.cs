using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_valor_positivo", "valor > 0");
            t.HasCheckConstraint("ck_orders_status", "status IN ('Pendente', 'Processando', 'Finalizado')");
        });

        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.Cliente).HasMaxLength(150).IsRequired();
        b.Property(o => o.Produto).HasMaxLength(150).IsRequired();
        b.Property(o => o.Valor).HasPrecision(18, 2);
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.IdempotencyKey).HasMaxLength(100);

        // Concorrencia otimista usando a coluna de sistema xmin do PostgreSQL.
        b.Property(o => o.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.HasIndex(o => o.Status);
        b.HasIndex(o => o.DataCriacao);
        b.HasIndex(o => o.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");

        b.HasMany(o => o.History)
            .WithOne()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("order_status_history");
        b.HasKey(h => h.Id);
        b.Property(h => h.Id).UseIdentityAlwaysColumn();
        b.Property(h => h.StatusAnterior).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.StatusNovo).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(h => new { h.OrderId, h.AlteradoEm });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.EventType).HasMaxLength(100);
        b.Property(m => m.RoutingKey).HasMaxLength(100);
        b.Property(m => m.Payload).HasColumnType("jsonb");
        b.Property(m => m.TraceParent).HasMaxLength(128);
        b.Property(m => m.TraceState).HasMaxLength(512);

        // Indice parcial: o relay so busca mensagens ainda nao publicadas.
        b.HasIndex(m => m.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("inbox_messages");
        b.HasKey(m => new { m.MessageId, m.Consumer });
        b.Property(m => m.Consumer).HasMaxLength(100);
    }
}

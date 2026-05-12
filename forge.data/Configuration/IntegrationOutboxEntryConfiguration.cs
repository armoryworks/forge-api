using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class IntegrationOutboxEntryConfiguration : IEntityTypeConfiguration<IntegrationOutboxEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationOutboxEntry> builder)
    {
        builder.ToTable("integration_outbox_entries");

        builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.OperationKey).HasMaxLength(200).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(4000);
        builder.Property(e => e.EntityType).HasMaxLength(100);

        // Unique index on idempotency key prevents duplicate enqueues for the same logical operation
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ix_integration_outbox_entries_idempotency_key");

        // Efficient worker scan: Pending/Failed rows ready for next attempt
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt })
            .HasDatabaseName("ix_integration_outbox_entries_status_next_attempt");

        // Lookup by entity (for admin panel drill-down)
        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .HasDatabaseName("ix_integration_outbox_entries_entity");

        // Provider filter for admin panel
        builder.HasIndex(e => new { e.Provider, e.Status })
            .HasDatabaseName("ix_integration_outbox_entries_provider_status");
    }
}

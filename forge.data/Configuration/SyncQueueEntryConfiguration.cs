using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SyncQueueEntryConfiguration : IEntityTypeConfiguration<SyncQueueEntry>
{
    public void Configure(EntityTypeBuilder<SyncQueueEntry> builder)
    {
        builder.ToTable("sync_queue_entries");

        builder.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Operation).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        // Efficient query for the worker: find pending entries ordered by age
        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_sync_queue_entries_status_created_at");

        // Efficient lookup of all queued entries for a given entity
        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .HasDatabaseName("ix_sync_queue_entries_entity_type_entity_id");
    }
}

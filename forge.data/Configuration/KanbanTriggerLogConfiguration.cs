using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class KanbanTriggerLogConfiguration : IEntityTypeConfiguration<KanbanTriggerLog>
{
    public void Configure(EntityTypeBuilder<KanbanTriggerLog> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.OrderType).HasMaxLength(50);
        builder.Property(e => e.RequestedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.FulfilledQuantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.KanbanCardId);
        builder.HasIndex(e => e.TriggeredAt);
        builder.HasIndex(e => e.TriggeredByUserId);
    }
}

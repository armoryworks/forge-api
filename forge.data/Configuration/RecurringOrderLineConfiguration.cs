using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecurringOrderLineConfiguration : IEntityTypeConfiguration<RecurringOrderLine>
{
    public void Configure(EntityTypeBuilder<RecurringOrderLine> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(500);
        // Phase 3 / WU-23 (F8-broad): decimal(18,4) for UoM-aware fractional qty.
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.RecurringOrderId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.IsHazmat).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.Description).HasMaxLength(500);
        // Phase 3 / WU-23 (F8-broad): decimal(18,4) for UoM-aware fractional qty.
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Weight).HasPrecision(12, 4);
        builder.Property(e => e.Length).HasPrecision(12, 4);
        builder.Property(e => e.Width).HasPrecision(12, 4);
        builder.Property(e => e.Height).HasPrecision(12, 4);
        builder.Property(e => e.HandlingInstructions).HasMaxLength(1000);
        builder.Property(e => e.SerialNumbers).HasMaxLength(2000);

        builder.HasIndex(e => e.ShipmentId);
        builder.HasIndex(e => e.SalesOrderLineId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.SalesOrderLine)
            .WithMany(s => s.ShipmentLines)
            .HasForeignKey(e => e.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}

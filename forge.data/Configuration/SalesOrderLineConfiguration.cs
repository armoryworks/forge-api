using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.Ignore(e => e.LineTotal);
        builder.Ignore(e => e.RemainingQuantity);
        builder.Ignore(e => e.IsFullyShipped);

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        // Phase 3 / WU-10 / F8-partial — quantity + price are decimal(18,4).
        // See PurchaseOrderLineConfiguration for rationale.
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.ShippedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Jobs)
            .WithOne(j => j.SalesOrderLine)
            .HasForeignKey(j => j.SalesOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.ShipmentLines)
            .WithOne(sl => sl.SalesOrderLine)
            .HasForeignKey(sl => sl.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

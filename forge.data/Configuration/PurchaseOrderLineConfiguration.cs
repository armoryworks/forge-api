using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.Ignore(e => e.RemainingQuantity);
        builder.Ignore(e => e.UnreceivedQuantity);

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        // Phase 3 / WU-10 / F8-partial — quantity + price are decimal(18,4).
        // 4 decimals on quantity is enough for any reasonable UoM (lb/kg/hr/in).
        // 4 decimals on unit price is industry-standard (allows fractional cents
        // on long-tail commodities — e.g. $0.0125 per fastener).
        builder.Property(e => e.OrderedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.ReceivedQuantity).HasPrecision(18, 4);
        // Phase 3 / WU-14 / H3 — short-close. Same precision as Ordered/Received.
        builder.Property(e => e.CancelledShortCloseQuantity).HasPrecision(18, 4).HasDefaultValue(0m);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.PurchaseOrderId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.PurchaseOrderLines)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.ReceivingRecords)
            .WithOne(r => r.PurchaseOrderLine)
            .HasForeignKey(r => r.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.MrpPlannedOrderId);
        builder.HasOne(e => e.MrpPlannedOrder)
            .WithMany()
            .HasForeignKey(e => e.MrpPlannedOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

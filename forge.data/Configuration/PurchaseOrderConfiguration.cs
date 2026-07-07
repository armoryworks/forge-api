using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(e => e.OriginSource).HasConversion<string>().HasMaxLength(30);

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.IsBlanket).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.Incoterm).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.BlanketRemainingQuantity);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.Property(e => e.PONumber).HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        // Phase 3 / WU-14 / H3 — short-close reason audit field.
        builder.Property(e => e.ShortCloseReason).HasMaxLength(2000);
        builder.Property(e => e.BlanketTotalQuantity).HasPrecision(18, 4);
        builder.Property(e => e.BlanketReleasedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.AgreedUnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        // Bought-parts effort PR2 — landed cost foundation.
        builder.Property(e => e.EstimatedFreight).HasPrecision(18, 4);
        builder.Property(e => e.QuoteCurrency).HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(e => e.FxRate).HasPrecision(18, 8);
        builder.Property(e => e.FxRateSource).HasMaxLength(200);

        builder.HasIndex(e => e.PONumber).IsUnique();
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Vendor)
            .WithMany(v => v.PurchaseOrders)
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany(j => j.PurchaseOrders)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.PurchaseOrder)
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

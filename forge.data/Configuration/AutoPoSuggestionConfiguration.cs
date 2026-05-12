using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AutoPoSuggestionConfiguration : IEntityTypeConfiguration<AutoPoSuggestion>
{
    public void Configure(EntityTypeBuilder<AutoPoSuggestion> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.SourceSalesOrderIds).HasColumnType("jsonb");

        // Phase 3 / WU-23 (F8-broad): decimal(18,4) for UoM-aware fractional qty.
        builder.Property(e => e.SuggestedQty).HasPrecision(18, 4);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ConvertedPurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.ConvertedPurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.PartId, e.Status });
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.ConvertedPurchaseOrderId)
            .HasFilter("converted_purchase_order_id IS NOT NULL");
    }
}

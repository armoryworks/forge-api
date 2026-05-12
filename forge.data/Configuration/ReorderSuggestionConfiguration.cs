using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ReorderSuggestionConfiguration : IEntityTypeConfiguration<ReorderSuggestion>
{
    public void Configure(EntityTypeBuilder<ReorderSuggestion> builder)
    {
        builder.Property(e => e.CurrentStock).HasPrecision(18, 4);
        builder.Property(e => e.AvailableStock).HasPrecision(18, 4);
        builder.Property(e => e.BurnRateDailyAvg).HasPrecision(18, 6);
        builder.Property(e => e.IncomingPoQuantity).HasPrecision(18, 4);
        builder.Property(e => e.SuggestedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.DismissReason).HasMaxLength(500);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.PartId, e.Status });

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ResultingPurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.ResultingPurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PurchaseOrderReleaseConfiguration : IEntityTypeConfiguration<PurchaseOrderRelease>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderRelease> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => new { e.PurchaseOrderId, e.ReleaseNumber }).IsUnique();
        builder.HasIndex(e => e.PurchaseOrderLineId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.PurchaseOrder)
            .WithMany(po => po.Releases)
            .HasForeignKey(e => e.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PurchaseOrderLine)
            .WithMany(l => l.Releases)
            .HasForeignKey(e => e.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReceivingRecord)
            .WithMany()
            .HasForeignKey(e => e.ReceivingRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

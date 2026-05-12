using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LotRecordConfiguration : IEntityTypeConfiguration<LotRecord>
{
    public void Configure(EntityTypeBuilder<LotRecord> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.SupplierLotNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        // Phase 3 / WU-23 (F8-broad): decimal(18,4) for UoM-aware fractional qty.
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.LotNumber).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.ProductionRunId);
        builder.HasIndex(e => e.PurchaseOrderLineId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ProductionRun)
            .WithMany()
            .HasForeignKey(e => e.ProductionRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

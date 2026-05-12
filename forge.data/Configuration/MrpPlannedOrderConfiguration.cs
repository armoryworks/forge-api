using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MrpPlannedOrderConfiguration : IEntityTypeConfiguration<MrpPlannedOrder>
{
    public void Configure(EntityTypeBuilder<MrpPlannedOrder> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.MrpRunId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.Status);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ReleasedPurchaseOrderId);
        builder.HasOne(e => e.ReleasedPurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.ReleasedPurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.ReleasedJobId);
        builder.HasOne(e => e.ReleasedJob)
            .WithMany()
            .HasForeignKey(e => e.ReleasedJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.ParentPlannedOrderId);
        builder.HasMany(e => e.ChildPlannedOrders)
            .WithOne(c => c.ParentPlannedOrder)
            .HasForeignKey(c => c.ParentPlannedOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

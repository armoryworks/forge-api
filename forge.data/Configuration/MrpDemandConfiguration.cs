using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MrpDemandConfiguration : IEntityTypeConfiguration<MrpDemand>
{
    public void Configure(EntityTypeBuilder<MrpDemand> builder)
    {
        builder.HasIndex(e => e.MrpRunId);
        builder.HasIndex(e => e.PartId);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ParentPlannedOrderId);
        builder.HasOne(e => e.ParentPlannedOrder)
            .WithMany(p => p.DependentDemands)
            .HasForeignKey(e => e.ParentPlannedOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

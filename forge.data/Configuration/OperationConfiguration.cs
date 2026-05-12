using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Instructions).HasMaxLength(4000);
        builder.Property(e => e.QcCriteria).HasMaxLength(1000);
        builder.Property(e => e.SetupMinutes).HasPrecision(10, 2);
        builder.Property(e => e.RunMinutesEach).HasPrecision(10, 2);
        builder.Property(e => e.RunMinutesLot).HasPrecision(10, 2);
        builder.Property(e => e.OverlapPercent).HasPrecision(5, 2);
        builder.Property(e => e.ScrapFactor).HasPrecision(5, 4);
        builder.Property(e => e.SubcontractCost).HasPrecision(18, 4);
        // Phase 3 H5 / WU-13 — subcontract turn time on the routing-op
        // contract (separate from the int SubcontractLeadTimeDays
        // scheduling field). Half-day granularity → precision (5,1).
        builder.Property(e => e.SubcontractTurnTimeDays).HasPrecision(5, 1);
        builder.Property(e => e.LaborRate).HasPrecision(18, 4);
        builder.Property(e => e.BurdenRate).HasPrecision(18, 4);
        builder.Property(e => e.EstimatedLaborCost).HasPrecision(18, 4);
        builder.Property(e => e.EstimatedBurdenCost).HasPrecision(18, 4);

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.AssetId);
        builder.HasIndex(e => e.ReferencedOperationId);

        builder.HasOne(e => e.Part)
            .WithMany(p => p.Operations)
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.WorkCenter)
            .WithMany(w => w.Operations)
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.SubcontractVendor)
            .WithMany()
            .HasForeignKey(e => e.SubcontractVendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ReferencedOperation)
            .WithMany()
            .HasForeignKey(e => e.ReferencedOperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ProductionRunConfiguration : IEntityTypeConfiguration<ProductionRun>
{
    public void Configure(EntityTypeBuilder<ProductionRun> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.ReworkQuantity).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.ReceivedQuantity).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.HasOne(pr => pr.Job)
            .WithMany()
            .HasForeignKey(pr => pr.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pr => pr.Part)
            .WithMany()
            .HasForeignKey(pr => pr.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pr => pr.JobId);
        builder.HasIndex(pr => pr.PartId);
        builder.HasIndex(pr => pr.Status);
        builder.HasIndex(pr => pr.RunNumber).IsUnique();

        builder.Property(pr => pr.RunNumber).HasMaxLength(50);
        builder.Property(pr => pr.Notes).HasMaxLength(2000);
        builder.Property(pr => pr.SetupTimeMinutes).HasPrecision(10, 2);
        builder.Property(pr => pr.RunTimeMinutes).HasPrecision(10, 2);
        builder.Property(pr => pr.IdealCycleTimeSeconds).HasPrecision(10, 2);
        builder.Property(pr => pr.ActualCycleTimeSeconds).HasPrecision(10, 2);

        builder.HasIndex(pr => pr.WorkCenterId);

        builder.HasOne(pr => pr.WorkCenter)
            .WithMany()
            .HasForeignKey(pr => pr.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

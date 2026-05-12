using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WorkCenterConfiguration : IEntityTypeConfiguration<WorkCenter>
{
    public void Configure(EntityTypeBuilder<WorkCenter> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.DailyCapacityHours).HasPrecision(8, 2);
        builder.Property(e => e.EfficiencyPercent).HasPrecision(5, 2);
        builder.Property(e => e.LaborCostPerHour).HasPrecision(18, 4);
        builder.Property(e => e.BurdenRatePerHour).HasPrecision(18, 4);
        builder.Property(e => e.IdealCycleTimeSeconds).HasPrecision(10, 2);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.CompanyLocationId);
        builder.HasIndex(e => e.AssetId);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.CompanyLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

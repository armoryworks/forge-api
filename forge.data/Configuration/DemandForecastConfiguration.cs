using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DemandForecastConfiguration : IEntityTypeConfiguration<DemandForecast>
{
    public void Configure(EntityTypeBuilder<DemandForecast> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.ForecastDataJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedByUserId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AppliedToMasterSchedule)
            .WithMany()
            .HasForeignKey(e => e.AppliedToMasterScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Overrides)
            .WithOne(o => o.DemandForecast)
            .HasForeignKey(o => o.DemandForecastId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

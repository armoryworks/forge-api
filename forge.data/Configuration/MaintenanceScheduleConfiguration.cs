using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.IntervalHours).HasPrecision(18, 2);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Logs)
            .WithOne(e => e.Schedule)
            .HasForeignKey(e => e.MaintenanceScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MaintenanceJob)
            .WithMany()
            .HasForeignKey(e => e.MaintenanceJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.AssetId);
        builder.HasIndex(e => e.NextDueAt);
        builder.HasIndex(e => e.MaintenanceJobId);
    }
}

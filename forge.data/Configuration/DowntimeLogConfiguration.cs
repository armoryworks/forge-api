using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DowntimeLogConfiguration : IEntityTypeConfiguration<DowntimeLog>
{
    public void Configure(EntityTypeBuilder<DowntimeLog> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.DurationHours);
        builder.Ignore(e => e.DurationMinutes);

        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Resolution).HasMaxLength(500);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(50);

        builder.HasIndex(e => e.AssetId);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.StartedAt);

        builder.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

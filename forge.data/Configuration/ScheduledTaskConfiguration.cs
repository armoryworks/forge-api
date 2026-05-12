using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ScheduledTaskConfiguration : IEntityTypeConfiguration<ScheduledTask>
{
    public void Configure(EntityTypeBuilder<ScheduledTask> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.CronExpression).HasMaxLength(100);

        builder.HasOne(t => t.TrackType)
            .WithMany()
            .HasForeignKey(t => t.TrackTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.InternalProjectType)
            .WithMany()
            .HasForeignKey(t => t.InternalProjectTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.TrackTypeId);
        builder.HasIndex(t => t.IsActive);
        builder.HasIndex(t => t.NextRunAt);
    }
}

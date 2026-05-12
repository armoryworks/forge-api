using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ScheduledOperationConfiguration : IEntityTypeConfiguration<ScheduledOperation>
{
    public void Configure(EntityTypeBuilder<ScheduledOperation> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.SetupHours).HasPrecision(10, 4);
        builder.Property(e => e.RunHours).HasPrecision(10, 4);
        builder.Property(e => e.TotalHours).HasPrecision(10, 4);

        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.OperationId);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.ScheduleRunId);
        builder.HasIndex(e => new { e.WorkCenterId, e.ScheduledStart });

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ScheduleRun)
            .WithMany(r => r.Operations)
            .HasForeignKey(e => e.ScheduleRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

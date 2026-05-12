using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class JobActivityLogConfiguration : IEntityTypeConfiguration<JobActivityLog>
{
    public void Configure(EntityTypeBuilder<JobActivityLog> builder)
    {
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.OperationId);

        builder.Property(e => e.FieldName).HasMaxLength(100);
        builder.Property(e => e.OldValue).HasMaxLength(1000);
        builder.Property(e => e.NewValue).HasMaxLength(1000);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne(e => e.Job)
            .WithMany(j => j.ActivityLogs)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on delete: a work center / operation can't be hard-deleted
        // while activity logs reference it. Soft-delete keeps the row alive
        // and audit history stays valid.
        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

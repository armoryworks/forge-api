using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ScheduleMilestoneConfiguration : IEntityTypeConfiguration<ScheduleMilestone>
{
    public void Configure(EntityTypeBuilder<ScheduleMilestone> builder)
    {
        builder.HasIndex(e => e.SalesOrderLineId);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.MilestoneType);

        builder.Property(e => e.MilestoneType).HasMaxLength(50);
        builder.Property(e => e.Notes).HasColumnType("text");

        builder.HasOne(e => e.SalesOrderLine)
            .WithMany()
            .HasForeignKey(e => e.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AndonAlertConfiguration : IEntityTypeConfiguration<AndonAlert>
{
    public void Configure(EntityTypeBuilder<AndonAlert> builder)
    {
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.WorkCenterId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.RequestedById);
        builder.HasIndex(e => e.AcknowledgedById);
        builder.HasIndex(e => e.ResolvedById);
        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.RequestedAt);
    }
}

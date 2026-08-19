using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceStepInstanceConfiguration : IEntityTypeConfiguration<SequenceStepInstance>
{
    public void Configure(EntityTypeBuilder<SequenceStepInstance> builder)
    {
        builder.Property(e => e.StepKey).HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SkipReason).HasMaxLength(2000);
        builder.HasIndex(e => new { e.InstanceId, e.StepKey }).IsUnique();
        builder.HasIndex(e => e.DwellExpiresAt).HasFilter("\"dwell_fired_at\" IS NULL AND \"dwell_expires_at\" IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceGateInstanceConfiguration : IEntityTypeConfiguration<SequenceGateInstance>
{
    public void Configure(EntityTypeBuilder<SequenceGateInstance> builder)
    {
        builder.Property(e => e.StepKey).HasMaxLength(100);
        builder.Property(e => e.GateKey).HasMaxLength(100);
        builder.Property(e => e.Verdict).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Reason).HasMaxLength(2000);
        builder.Property(e => e.OverrideReason).HasMaxLength(2000);
        builder.HasIndex(e => new { e.InstanceId, e.StepKey, e.GateKey }).IsUnique();
    }
}

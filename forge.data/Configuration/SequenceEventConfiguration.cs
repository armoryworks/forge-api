using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceEventConfiguration : IEntityTypeConfiguration<SequenceEvent>
{
    public void Configure(EntityTypeBuilder<SequenceEvent> builder)
    {
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.StepKey).HasMaxLength(100);
        builder.Property(e => e.GateKey).HasMaxLength(100);
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.InstanceId, e.OccurredAt });
    }
}

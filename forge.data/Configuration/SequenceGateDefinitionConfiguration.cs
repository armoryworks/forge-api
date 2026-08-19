using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceGateDefinitionConfiguration : IEntityTypeConfiguration<SequenceGateDefinition>
{
    public void Configure(EntityTypeBuilder<SequenceGateDefinition> builder)
    {
        builder.Property(e => e.StepKey).HasMaxLength(100);
        builder.Property(e => e.Key).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.ConfigJson).HasColumnType("jsonb");
        builder.Property(e => e.ExpiryAction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EscalateRole).HasMaxLength(100);
        builder.HasIndex(e => new { e.DefinitionId, e.StepKey, e.Key }).IsUnique();
    }
}

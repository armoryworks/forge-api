using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceStepDefinitionConfiguration : IEntityTypeConfiguration<SequenceStepDefinition>
{
    public void Configure(EntityTypeBuilder<SequenceStepDefinition> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.JoinPolicy).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.DwellExpiryAction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EscalateRole).HasMaxLength(100);
        builder.HasIndex(e => new { e.DefinitionId, e.Key }).IsUnique();
    }
}

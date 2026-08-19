using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceEdgeDefinitionConfiguration : IEntityTypeConfiguration<SequenceEdgeDefinition>
{
    public void Configure(EntityTypeBuilder<SequenceEdgeDefinition> builder)
    {
        builder.Property(e => e.FromStepKey).HasMaxLength(100);
        builder.Property(e => e.ToStepKey).HasMaxLength(100);
        builder.HasIndex(e => new { e.DefinitionId, e.FromStepKey, e.ToStepKey }).IsUnique();
    }
}

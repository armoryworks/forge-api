using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class AiProvenanceConfiguration : IEntityTypeConfiguration<AiProvenance>
{
    public void Configure(EntityTypeBuilder<AiProvenance> builder)
    {
        builder.Property(e => e.EntityType).HasMaxLength(60);
        builder.Property(e => e.Model).HasMaxLength(120);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => new { e.EntityType, e.EntityId })
            .IsUnique()
            .HasDatabaseName("ix_ai_provenances_entity");
    }
}

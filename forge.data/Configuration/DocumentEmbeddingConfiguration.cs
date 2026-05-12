using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class DocumentEmbeddingConfiguration : IEntityTypeConfiguration<DocumentEmbedding>
{
    public void Configure(EntityTypeBuilder<DocumentEmbedding> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ChunkText).IsRequired().HasMaxLength(8000);
        builder.Property(e => e.SourceField).HasMaxLength(100);
        builder.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Embedding).HasColumnType("vector(384)");

        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}

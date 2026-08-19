using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SequenceDefinitionConfiguration : IEntityTypeConfiguration<SequenceDefinition>
{
    public void Configure(EntityTypeBuilder<SequenceDefinition> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Property(e => e.Code).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.SubjectEntityType).HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // One row per (code, version) among live rows.
        builder.HasIndex(e => new { e.Code, e.Version }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Steps).WithOne(s => s.Definition).HasForeignKey(s => s.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Edges).WithOne(s => s.Definition).HasForeignKey(s => s.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Gates).WithOne(s => s.Definition).HasForeignKey(s => s.DefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class TerminologyEntryConfiguration : IEntityTypeConfiguration<TerminologyEntry>
{
    public void Configure(EntityTypeBuilder<TerminologyEntry> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(500).IsRequired();

        builder.HasIndex(e => e.Key).IsUnique();

        // Pro Services rollout (Artifact 4 §3.1) — preset / admin-edit
        // provenance. IsAdminEdited defaults to false; rows created via
        // admin UI set it to true so re-applied presets respect the edit.
        builder.Property(e => e.IsAdminEdited).HasDefaultValue(false);
        builder.Property(e => e.SourcePresetId).HasMaxLength(50);
        builder.HasIndex(e => e.SourcePresetId);
    }
}

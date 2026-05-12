using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class FormDefinitionVersionConfiguration : IEntityTypeConfiguration<FormDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<FormDefinitionVersion> builder)
    {
        builder.Property(e => e.StateCode).HasMaxLength(10);
        builder.Property(e => e.FormDefinitionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SourceUrl).HasMaxLength(500);
        builder.Property(e => e.Sha256Hash).HasMaxLength(64);
        builder.Property(e => e.Revision).HasMaxLength(50);
        builder.Property(e => e.VisualComparisonJson).HasColumnType("jsonb");

        // Lookup: current version for a template
        builder.HasIndex(e => new { e.TemplateId, e.EffectiveDate });

        // Lookup: current version for a state code
        builder.HasIndex(e => new { e.StateCode, e.EffectiveDate });

        // FK to ComplianceFormTemplate (optional)
        builder.HasOne(e => e.Template)
            .WithMany(t => t.FormDefinitionVersions)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Check constraint: at least one of TemplateId or StateCode must be set
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_form_definition_versions_scope",
            "template_id IS NOT NULL OR state_code IS NOT NULL"));
    }
}

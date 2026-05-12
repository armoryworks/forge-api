using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ComplianceFormTemplateConfiguration : IEntityTypeConfiguration<ComplianceFormTemplate>
{
    public void Configure(EntityTypeBuilder<ComplianceFormTemplate> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Icon).HasMaxLength(50);
        builder.Property(e => e.SourceUrl).HasMaxLength(500);
        builder.Property(e => e.Sha256Hash).HasMaxLength(64);
        builder.Property(e => e.ProfileCompletionKey).HasMaxLength(50);
        builder.Property(e => e.AcroFieldMapJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.FormType);

        builder.HasOne(e => e.ManualOverrideFile)
            .WithMany()
            .HasForeignKey(e => e.ManualOverrideFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.FilledPdfTemplate)
            .WithMany()
            .HasForeignKey(e => e.FilledPdfTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

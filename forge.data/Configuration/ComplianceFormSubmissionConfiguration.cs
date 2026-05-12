using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ComplianceFormSubmissionConfiguration : IEntityTypeConfiguration<ComplianceFormSubmission>
{
    public void Configure(EntityTypeBuilder<ComplianceFormSubmission> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.DocuSealSubmitUrl).HasMaxLength(1000);
        builder.Property(e => e.FormDataJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.UserId, e.TemplateId });

        builder.HasOne(e => e.Template)
            .WithMany(t => t.Submissions)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);

        builder.HasOne(e => e.SignedPdfFile)
            .WithMany()
            .HasForeignKey(e => e.SignedPdfFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.FormDefinitionVersion)
            .WithMany(v => v.Submissions)
            .HasForeignKey(e => e.FormDefinitionVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.FormDefinitionVersionId);

        builder.HasOne(e => e.FilledPdfFile)
            .WithMany()
            .HasForeignKey(e => e.FilledPdfFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.I9DocumentListType).HasMaxLength(3);
        builder.Property(e => e.I9DocumentDataJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.I9EmployerUserId);
        builder.HasIndex(e => e.I9Section2OverdueAt);
        builder.HasIndex(e => e.I9ReverificationDueAt);
    }
}

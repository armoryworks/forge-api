using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class FmeaAnalysisConfiguration : IEntityTypeConfiguration<FmeaAnalysis>
{
    public void Configure(EntityTypeBuilder<FmeaAnalysis> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.FmeaNumber).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.PreparedBy).HasMaxLength(200);
        builder.Property(e => e.Responsibility).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(4000);

        builder.HasIndex(e => e.FmeaNumber).IsUnique();
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.OperationId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.PpapSubmissionId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Operation)
            .WithMany()
            .HasForeignKey(e => e.OperationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.PpapSubmission)
            .WithMany()
            .HasForeignKey(e => e.PpapSubmissionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class QcInspectionConfiguration : IEntityTypeConfiguration<QcInspection>
{
    public void Configure(EntityTypeBuilder<QcInspection> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Status).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.JobId);
        builder.HasIndex(e => e.ProductionRunId);
        builder.HasIndex(e => e.TemplateId);
        builder.HasIndex(e => e.InspectorId);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ProductionRun)
            .WithMany()
            .HasForeignKey(e => e.ProductionRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Results)
            .WithOne(r => r.Inspection)
            .HasForeignKey(r => r.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

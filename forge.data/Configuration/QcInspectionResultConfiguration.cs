using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class QcInspectionResultConfiguration : IEntityTypeConfiguration<QcInspectionResult>
{
    public void Configure(EntityTypeBuilder<QcInspectionResult> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Property(e => e.MeasuredValue).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasIndex(e => e.InspectionId);
        builder.HasIndex(e => e.ChecklistItemId);

        builder.HasOne(e => e.ChecklistItem)
            .WithMany()
            .HasForeignKey(e => e.ChecklistItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

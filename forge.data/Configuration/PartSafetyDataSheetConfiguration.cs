using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartSafetyDataSheetConfiguration : IEntityTypeConfiguration<PartSafetyDataSheet>
{
    public void Configure(EntityTypeBuilder<PartSafetyDataSheet> builder)
    {
        builder.Property(e => e.SdsType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Supplier).HasMaxLength(200);

        builder.HasIndex(e => e.PartId).HasDatabaseName("ix_part_safety_data_sheets_part_id");
        builder.HasIndex(e => e.DocumentSetId).HasDatabaseName("ix_part_safety_data_sheets_document_set_id");

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .HasConstraintName("fk_part_safety_data_sheets__parts_part_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentSet>()
            .WithMany()
            .HasForeignKey(e => e.DocumentSetId)
            .HasConstraintName("fk_part_safety_data_sheets__document_sets_document_set_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

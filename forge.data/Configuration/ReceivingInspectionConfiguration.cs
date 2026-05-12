using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ReceivingInspectionConfiguration : IEntityTypeConfiguration<ReceivingInspection>
{
    public void Configure(EntityTypeBuilder<ReceivingInspection> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.AcceptedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.RejectedQuantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.ReceivingRecordId);
        builder.HasIndex(e => e.InspectedById);
        builder.HasIndex(e => e.QcInspectionId);

        builder.HasOne(e => e.ReceivingRecord)
            .WithMany()
            .HasForeignKey(e => e.ReceivingRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.QcInspection)
            .WithMany()
            .HasForeignKey(e => e.QcInspectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

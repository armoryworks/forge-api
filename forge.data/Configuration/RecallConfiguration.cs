using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecallConfiguration : IEntityTypeConfiguration<Recall>
{
    public void Configure(EntityTypeBuilder<Recall> builder)
    {
        builder.Property(e => e.Reason).HasMaxLength(2000);
        builder.Property(e => e.ResolutionNotes).HasMaxLength(2000);
        builder.Property(e => e.TotalQuarantinedQuantity).HasPrecision(18, 4);

        builder.HasOne(e => e.InitiatedLot)
            .WithMany()
            .HasForeignKey(e => e.InitiatedLotId)
            .HasConstraintName("fk_recalls__lot_records_initiated_lot_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.AffectedLots)
            .WithOne(e => e.Recall)
            .HasForeignKey(e => e.RecallId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.AffectedShipments)
            .WithOne(e => e.Recall)
            .HasForeignKey(e => e.RecallId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

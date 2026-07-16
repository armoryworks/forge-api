using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecallAffectedLotConfiguration : IEntityTypeConfiguration<RecallAffectedLot>
{
    public void Configure(EntityTypeBuilder<RecallAffectedLot> builder)
    {
        builder.Property(e => e.ConsumedQuantity).HasPrecision(18, 6);
        builder.Property(e => e.OnHandQuantity).HasPrecision(18, 4);
        builder.Property(e => e.QuarantinedQuantity).HasPrecision(18, 4);

        builder.HasOne(e => e.Lot)
            .WithMany()
            .HasForeignKey(e => e.LotId)
            .HasConstraintName("fk_recall_affected_lots__lot_records_lot_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

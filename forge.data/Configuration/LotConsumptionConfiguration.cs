using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class LotConsumptionConfiguration : IEntityTypeConfiguration<LotConsumption>
{
    public void Configure(EntityTypeBuilder<LotConsumption> builder)
    {
        builder.Property(e => e.Quantity).HasPrecision(18, 6);

        builder.HasIndex(e => e.ConsumedLotId).HasDatabaseName("ix_lot_consumptions_consumed_lot_id");
        builder.HasIndex(e => e.ProducedLotId).HasDatabaseName("ix_lot_consumptions_produced_lot_id");

        builder.HasOne(e => e.ConsumedLot)
            .WithMany()
            .HasForeignKey(e => e.ConsumedLotId)
            .HasConstraintName("fk_lot_consumptions__lot_records_consumed_lot_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ProducedLot)
            .WithMany()
            .HasForeignKey(e => e.ProducedLotId)
            .HasConstraintName("fk_lot_consumptions__lot_records_produced_lot_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PickLineConfiguration : IEntityTypeConfiguration<PickLine>
{
    public void Configure(EntityTypeBuilder<PickLine> builder)
    {
        builder.Property(e => e.BinPath).HasMaxLength(500);
        builder.Property(e => e.RequestedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.PickedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.ShortNotes).HasMaxLength(500);

        builder.HasIndex(e => e.WaveId);
        builder.HasIndex(e => e.ShipmentLineId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.FromLocationId);
        builder.HasIndex(e => e.PickedByUserId);

        builder.HasOne(e => e.ShipmentLine)
            .WithMany()
            .HasForeignKey(e => e.ShipmentLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FromLocation)
            .WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

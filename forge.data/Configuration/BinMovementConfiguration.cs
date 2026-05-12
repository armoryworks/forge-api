using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BinMovementConfiguration : IEntityTypeConfiguration<BinMovement>
{
    public void Configure(EntityTypeBuilder<BinMovement> builder)
    {
        builder.Property(e => e.EntityType).HasMaxLength(50);
        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasOne(e => e.FromLocation)
            .WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToLocation)
            .WithMany()
            .HasForeignKey(e => e.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReversedMovement)
            .WithMany()
            .HasForeignKey(e => e.ReversedMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ScanActionLog)
            .WithMany()
            .HasForeignKey(e => e.ScanActionLogId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.MovedAt);
        builder.HasIndex(e => e.ReversedMovementId)
            .HasFilter("reversed_movement_id IS NOT NULL");
        builder.HasIndex(e => e.ScanActionLogId)
            .HasFilter("scan_action_log_id IS NOT NULL");
    }
}

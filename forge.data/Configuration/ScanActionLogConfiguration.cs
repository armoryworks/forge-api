using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ScanActionLogConfiguration : IEntityTypeConfiguration<ScanActionLog>
{
    public void Configure(EntityTypeBuilder<ScanActionLog> builder)
    {
        builder.Property(e => e.ActionType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.PartNumber).HasMaxLength(100);
        builder.Property(e => e.RelatedEntityType).HasMaxLength(50);
        builder.Property(e => e.KioskId).HasMaxLength(100);
        builder.Property(e => e.DeviceId).HasMaxLength(200);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FromLocation)
            .WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToLocation)
            .WithMany()
            .HasForeignKey(e => e.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.ActionType);
        builder.HasIndex(e => e.PartId).HasFilter("part_id IS NOT NULL");
        builder.HasIndex(e => e.ReversesLogId).HasFilter("reverses_log_id IS NOT NULL");
        builder.HasIndex(e => e.ReversedByLogId).HasFilter("reversed_by_log_id IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PhysicalInventoryTagConfiguration : IEntityTypeConfiguration<PhysicalInventoryTag>
{
    public void Configure(EntityTypeBuilder<PhysicalInventoryTag> builder)
    {
        builder.ToTable("physical_inventory_tags");

        builder.Ignore(e => e.FinalQuantity);

        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.SerialNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.FrozenQuantity).HasPrecision(18, 4);
        builder.Property(e => e.CountedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.RecountQuantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(18, 4);
        builder.Property(e => e.VarianceValue).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.PhysicalInventoryId, e.TagNumber }).IsUnique()
            .HasDatabaseName("ix_physical_inventory_tags_inventory_tag_number");
        builder.HasIndex(e => e.LocationId).HasDatabaseName("ix_physical_inventory_tags_location_id");
        builder.HasIndex(e => e.PartId).HasDatabaseName("ix_physical_inventory_tags_part_id");

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .HasConstraintName("fk_physical_inventory_tags_location")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.BinContent)
            .WithMany()
            .HasForeignKey(e => e.BinContentId)
            .HasConstraintName("fk_physical_inventory_tags_bin_content")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .HasConstraintName("fk_physical_inventory_tags_part")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BinMovement>()
            .WithMany()
            .HasForeignKey(e => e.BinMovementId)
            .HasConstraintName("fk_physical_inventory_tags_bin_movement")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

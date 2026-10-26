using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class InventoryStockLoadLineConfiguration : IEntityTypeConfiguration<InventoryStockLoadLine>
{
    public void Configure(EntityTypeBuilder<InventoryStockLoadLine> builder)
    {
        builder.ToTable("inventory_stock_load_lines");

        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(18, 4);
        builder.Property(e => e.ExtendedValue).HasPrecision(18, 2);

        builder.HasIndex(e => e.StockLoadId).HasDatabaseName("ix_inventory_stock_load_lines_stock_load_id");

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .HasConstraintName("fk_inventory_stock_load_lines_part")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.StorageLocation)
            .WithMany()
            .HasForeignKey(e => e.StorageLocationId)
            .HasConstraintName("fk_inventory_stock_load_lines_location")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BinContent>()
            .WithMany()
            .HasForeignKey(e => e.BinContentId)
            .HasConstraintName("fk_inventory_stock_load_lines_bin_content")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<BinMovement>()
            .WithMany()
            .HasForeignKey(e => e.BinMovementId)
            .HasConstraintName("fk_inventory_stock_load_lines_bin_movement")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

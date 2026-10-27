using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PhysicalInventoryConfiguration : IEntityTypeConfiguration<PhysicalInventory>
{
    public void Configure(EntityTypeBuilder<PhysicalInventory> builder)
    {
        builder.ToTable("physical_inventories");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.InventoryNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.RecountTolerancePercent).HasPrecision(9, 4);
        builder.Property(e => e.TotalVarianceValue).HasPrecision(18, 2);
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.HasIndex(e => e.InventoryNumber).IsUnique().HasDatabaseName("ix_physical_inventories_inventory_number");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_physical_inventories_status");
        builder.HasIndex(e => e.ScopeLocationId).HasDatabaseName("ix_physical_inventories_scope_location_id");

        builder.HasOne(e => e.ScopeLocation)
            .WithMany()
            .HasForeignKey(e => e.ScopeLocationId)
            .HasConstraintName("fk_physical_inventories_scope_location")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Tags)
            .WithOne(t => t.PhysicalInventory)
            .HasForeignKey(t => t.PhysicalInventoryId)
            .HasConstraintName("fk_physical_inventory_tags_physical_inventory")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

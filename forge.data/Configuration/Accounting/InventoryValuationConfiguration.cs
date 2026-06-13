using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class InventoryValuationConfiguration : IEntityTypeConfiguration<InventoryValuation>
{
    public void Configure(EntityTypeBuilder<InventoryValuation> builder)
    {
        builder.ToTable("acct_inventory_valuations");

        builder.Property(e => e.OnHandQuantity).HasPrecision(18, 4);
        builder.Property(e => e.AverageUnitCost).HasPrecision(18, 6);
        builder.Property(e => e.TotalValue).HasPrecision(18, 2);
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.HasIndex(e => new { e.BookId, e.PartId })
            .IsUnique()
            .HasDatabaseName("ux_acct_inventory_valuations_book_part");

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .HasConstraintName("fk_acct_inventory_valuations_part")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

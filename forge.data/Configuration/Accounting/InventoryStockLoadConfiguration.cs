using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class InventoryStockLoadConfiguration : IEntityTypeConfiguration<InventoryStockLoad>
{
    public void Configure(EntityTypeBuilder<InventoryStockLoad> builder)
    {
        builder.ToTable("inventory_stock_loads");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.LoadNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.SourceFileName).HasMaxLength(260);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.TotalValue).HasPrecision(18, 2);
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.HasIndex(e => e.LoadNumber).IsUnique().HasDatabaseName("ix_inventory_stock_loads_load_number");
        builder.HasIndex(e => e.BookId).HasDatabaseName("ix_inventory_stock_loads_book_id");

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_inventory_stock_loads_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<JournalEntry>()
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .HasConstraintName("fk_inventory_stock_loads_journal_entry")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.StockLoad)
            .HasForeignKey(l => l.StockLoadId)
            .HasConstraintName("fk_inventory_stock_load_lines_stock_load")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

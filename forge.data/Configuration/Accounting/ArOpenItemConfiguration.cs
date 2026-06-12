using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class ArOpenItemConfiguration : IEntityTypeConfiguration<ArOpenItem>
{
    public void Configure(EntityTypeBuilder<ArOpenItem> builder)
    {
        builder.ToTable("acct_ar_open_items");

        builder.Property(e => e.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.DocumentNumber).HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Money (18,4); FxRate (18,8) — matches the journal-line precision (§5.6).
        builder.Property(e => e.OriginalTxnAmount).HasPrecision(18, 4);
        builder.Property(e => e.OriginalFunctionalAmount).HasPrecision(18, 4);
        builder.Property(e => e.AppliedTxnAmount).HasPrecision(18, 4);
        builder.Property(e => e.AppliedFunctionalAmount).HasPrecision(18, 4);
        builder.Property(e => e.FxRate).HasPrecision(18, 8);

        // One open item per source document — the posting-time exists-guard's DB backstop.
        builder.HasIndex(e => new { e.SourceType, e.SourceId })
            .IsUnique()
            .HasDatabaseName("ux_acct_ar_open_items_source");

        builder.HasIndex(e => new { e.BookId, e.Status })
            .HasDatabaseName("ix_acct_ar_open_items_book_status");

        builder.HasIndex(e => e.CustomerId)
            .HasDatabaseName("ix_acct_ar_open_items_customer");

        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_ar_open_items_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Forge.Core.Entities.Currency>()
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .HasConstraintName("fk_acct_ar_open_items_currency")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

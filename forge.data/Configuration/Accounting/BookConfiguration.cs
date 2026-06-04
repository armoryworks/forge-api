using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("acct_books");

        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ReportingTimeZone).HasMaxLength(64).IsRequired();
        builder.Property(e => e.RoundingTolerance).HasPrecision(18, 4);
        // Per-book config (manufacturing defaults). Stored as strings; engine
        // consumes them in Phase 2 (costing) / Phase 1 (rev-rec) — present now so
        // they're config, not a later migration.
        builder.Property(e => e.DefaultCostingMethod)
            .HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(CostingMethod.Standard);
        builder.Property(e => e.RevenueRecognitionMethod)
            .HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(RevenueRecognitionMethod.PointInTime);

        builder.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_acct_books_code");

        builder.HasOne(e => e.FunctionalCurrency)
            .WithMany()
            .HasForeignKey(e => e.FunctionalCurrencyId)
            .HasConstraintName("fk_acct_books_currency")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

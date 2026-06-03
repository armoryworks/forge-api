using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        builder.ToTable("acct_fiscal_years");

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(e => new { e.BookId, e.Name })
            .IsUnique()
            .HasDatabaseName("ux_acct_fiscal_years_book_name");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_fiscal_years_book")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

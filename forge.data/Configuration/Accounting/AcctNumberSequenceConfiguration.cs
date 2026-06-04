using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class AcctNumberSequenceConfiguration : IEntityTypeConfiguration<AcctNumberSequence>
{
    public void Configure(EntityTypeBuilder<AcctNumberSequence> builder)
    {
        builder.ToTable("acct_number_sequences");

        // One counter per (book, fiscal-year).
        builder.HasIndex(e => new { e.BookId, e.FiscalYearId })
            .IsUnique()
            .HasDatabaseName("ux_acct_number_sequences_book_year");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_number_sequences_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FiscalYear)
            .WithMany()
            .HasForeignKey(e => e.FiscalYearId)
            .HasConstraintName("fk_acct_number_sequences_year")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

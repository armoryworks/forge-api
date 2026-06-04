using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("acct_journal_lines", t => t.HasCheckConstraint(
            "ck_acct_journal_lines_debit_xor_credit",
            "(debit = 0) <> (credit = 0)"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Money (18,4); FxRate (18,8) — matches ExchangeRate.Rate (§5.6).
        builder.Property(e => e.Debit).HasPrecision(18, 4);
        builder.Property(e => e.Credit).HasPrecision(18, 4);
        builder.Property(e => e.TxnAmount).HasPrecision(18, 4);
        builder.Property(e => e.FunctionalAmount).HasPrecision(18, 4);
        builder.Property(e => e.FxRate).HasPrecision(18, 8);

        builder.Property(e => e.SubledgerPartyType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.JournalEntryId).HasDatabaseName("ix_acct_journal_lines_entry");
        builder.HasIndex(e => e.GlAccountId).HasDatabaseName("ix_acct_journal_lines_account");
        builder.HasIndex(e => new { e.SubledgerPartyType, e.SubledgerPartyId })
            .HasDatabaseName("ix_acct_journal_lines_party");

        // FK back to the header is configured on JournalEntry (HasMany).
        // All remaining JournalLine FKs are DeleteBehavior.Restrict (§5.6).
        builder.HasOne(e => e.GlAccount)
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .HasConstraintName("fk_acct_journal_lines_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .HasConstraintName("fk_acct_journal_lines_job")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CostCenter)
            .WithMany()
            .HasForeignKey(e => e.CostCenterId)
            .HasConstraintName("fk_acct_journal_lines_cost_center")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .HasConstraintName("fk_acct_journal_lines_currency")
            .OnDelete(DeleteBehavior.Restrict);

        // BookId is denormalized for book-consistency checks; index it but no FK
        // navigation (the entry's Book FK is the authoritative link).
        builder.HasIndex(e => e.BookId).HasDatabaseName("ix_acct_journal_lines_book");
    }
}

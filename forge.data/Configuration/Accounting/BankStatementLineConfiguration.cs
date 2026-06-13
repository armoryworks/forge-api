using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

/// <summary>
/// ⚡ BANK-001 — staged statement line. The (cash account, FITID) unique index IS the import
/// idempotency: a re-imported or overlapping file inserts nothing twice.
/// </summary>
public class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("acct_bank_statement_lines");

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Description).HasMaxLength(256);
        builder.Property(e => e.Fitid).HasMaxLength(128).IsRequired();
        builder.Property(e => e.MatchStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(e => new { e.CashGlAccountId, e.Fitid })
            .IsUnique()
            .HasDatabaseName("ux_acct_stmt_lines_account_fitid");
        builder.HasIndex(e => e.MatchStatus).HasDatabaseName("ix_acct_stmt_lines_status");
        builder.HasIndex(e => e.MatchedJournalLineId).HasDatabaseName("ix_acct_stmt_lines_journal_line");

        builder.HasOne(e => e.MatchedJournalLine)
            .WithMany()
            .HasForeignKey(e => e.MatchedJournalLineId)
            .HasConstraintName("fk_acct_stmt_lines_journal_line")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

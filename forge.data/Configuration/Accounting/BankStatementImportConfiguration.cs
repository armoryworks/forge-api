using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

/// <summary>⚡ BANK-001 — statement import header (status rollups computed from lines on read).</summary>
public class BankStatementImportConfiguration : IEntityTypeConfiguration<BankStatementImport>
{
    public void Configure(EntityTypeBuilder<BankStatementImport> builder)
    {
        builder.ToTable("acct_bank_statement_imports");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.FileName).HasMaxLength(260).IsRequired();
        builder.Property(e => e.Format).HasConversion<string>().HasMaxLength(10).IsRequired();

        builder.HasIndex(e => new { e.BookId, e.CashGlAccountId })
            .HasDatabaseName("ix_acct_stmt_imports_book_account");

        builder.HasOne<GlAccount>()
            .WithMany()
            .HasForeignKey(e => e.CashGlAccountId)
            .HasConstraintName("fk_acct_stmt_imports_cash_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Import)
            .HasForeignKey(l => l.BankStatementImportId)
            .HasConstraintName("fk_acct_stmt_lines_import")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

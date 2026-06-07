using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliation>
{
    public void Configure(EntityTypeBuilder<BankReconciliation> builder)
    {
        builder.ToTable("acct_bank_reconciliations");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.StatementEndingBalance).HasPrecision(18, 2);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.HasIndex(e => new { e.BookId, e.CashGlAccountId, e.StatementDate })
            .HasDatabaseName("ix_acct_bank_recs_book_account_date");

        builder.HasOne<GlAccount>()
            .WithMany()
            .HasForeignKey(e => e.CashGlAccountId)
            .HasConstraintName("fk_acct_bank_recs_cash_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Items)
            .WithOne(i => i.BankReconciliation)
            .HasForeignKey(i => i.BankReconciliationId)
            .HasConstraintName("fk_acct_bank_rec_items_rec")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankReconciliationItemConfiguration : IEntityTypeConfiguration<BankReconciliationItem>
{
    public void Configure(EntityTypeBuilder<BankReconciliationItem> builder)
    {
        builder.ToTable("acct_bank_reconciliation_items");

        builder.HasIndex(e => new { e.BankReconciliationId, e.JournalLineId })
            .IsUnique()
            .HasDatabaseName("ux_acct_bank_rec_items_rec_line");

        builder.HasIndex(e => e.JournalLineId).HasDatabaseName("ix_acct_bank_rec_items_line");

        builder.HasOne(e => e.JournalLine)
            .WithMany()
            .HasForeignKey(e => e.JournalLineId)
            .HasConstraintName("fk_acct_bank_rec_items_line")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

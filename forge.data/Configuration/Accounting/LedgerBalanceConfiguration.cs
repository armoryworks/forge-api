using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class LedgerBalanceConfiguration : IEntityTypeConfiguration<LedgerBalance>
{
    public void Configure(EntityTypeBuilder<LedgerBalance> builder)
    {
        builder.ToTable("acct_ledger_balances");

        builder.Property(e => e.DebitTotal).HasPrecision(18, 4);
        builder.Property(e => e.CreditTotal).HasPrecision(18, 4);

        // One row per grain (BookId, GlAccountId, FiscalPeriodId, CurrencyId).
        builder.HasIndex(e => new { e.BookId, e.GlAccountId, e.FiscalPeriodId, e.CurrencyId })
            .IsUnique()
            .HasDatabaseName("ux_acct_ledger_balances_grain");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_ledger_balances_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.GlAccount)
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .HasConstraintName("fk_acct_ledger_balances_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FiscalPeriod)
            .WithMany()
            .HasForeignKey(e => e.FiscalPeriodId)
            .HasConstraintName("fk_acct_ledger_balances_period")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .HasConstraintName("fk_acct_ledger_balances_currency")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

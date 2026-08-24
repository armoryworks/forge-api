using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class AcctBudgetConfiguration : IEntityTypeConfiguration<AcctBudget>
{
    public void Configure(EntityTypeBuilder<AcctBudget> builder)
    {
        builder.ToTable("acct_budgets");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.BookId, e.GlAccountId, e.FiscalYear, e.PeriodMonth })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_acct_budgets_book_account_year_period");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_budgets_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.GlAccount)
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .HasConstraintName("fk_acct_budgets_gl_account")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

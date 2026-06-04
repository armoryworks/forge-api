using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class AccountDeterminationRuleConfiguration : IEntityTypeConfiguration<AccountDeterminationRule>
{
    public void Configure(EntityTypeBuilder<AccountDeterminationRule> builder)
    {
        builder.ToTable("acct_account_determination_rules");

        builder.Property(e => e.Key).HasMaxLength(50).IsRequired();

        // One rule per (book, key, scope). Phase 0 seeds only global rows
        // (all scope columns null); Phase 2 adds more-specific scoped rows.
        builder.HasIndex(e => new { e.BookId, e.Key, e.ItemId, e.CategoryId, e.ValuationClassId })
            .IsUnique()
            .HasDatabaseName("ux_acct_determination_book_key_scope");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_determination_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.GlAccount)
            .WithMany()
            .HasForeignKey(e => e.GlAccountId)
            .HasConstraintName("fk_acct_determination_account")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

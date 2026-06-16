using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities.Accounting;

namespace Forge.Data.Configuration.Accounting;

public class GlAccountConfiguration : IEntityTypeConfiguration<GlAccount>
{
    public void Configure(EntityTypeBuilder<GlAccount> builder)
    {
        builder.ToTable("acct_gl_accounts");

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.RequiresCostCenter).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.RequiresJob).HasDefaultValueSql("false").ValueGeneratedNever();

        builder.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.AccountType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.NormalBalance).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.ControlType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CashFlowCategory).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(1000);

        // Account numbers are unique within a book.
        builder.HasIndex(e => new { e.BookId, e.AccountNumber })
            .IsUnique()
            .HasDatabaseName("ux_acct_gl_accounts_book_number");

        builder.HasOne(e => e.Book)
            .WithMany()
            .HasForeignKey(e => e.BookId)
            .HasConstraintName("fk_acct_gl_accounts_book")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ParentAccount)
            .WithMany(p => p.ChildAccounts)
            .HasForeignKey(e => e.ParentAccountId)
            .HasConstraintName("fk_acct_gl_accounts_parent")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ParentAccountId).HasDatabaseName("ix_acct_gl_accounts_parent");
    }
}

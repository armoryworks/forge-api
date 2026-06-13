using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Category).HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.ReceiptFileId).HasMaxLength(200);
        builder.Property(e => e.ApprovalNotes).HasMaxLength(500);
        builder.Property(e => e.ExternalExpenseId).HasMaxLength(100);

        // Phase-1 STAGE C — persist the settlement target as its string name so
        // the column is stable/readable and not positional (additive, nullable).
        builder.Property(e => e.SettlementTarget)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        // Optional vendor the expense settles to (AP sub-ledger party). SetNull
        // mirrors the Job FK: removing a vendor must not delete its expenses.
        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.VendorId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>⚡ ACCOUNTING BOUNDARY — vendor credit and debit memos (AP).</summary>
public class VendorCreditMemoConfiguration : IEntityTypeConfiguration<VendorCreditMemo>
{
    public void Configure(EntityTypeBuilder<VendorCreditMemo> builder)
    {
        builder.ToTable("vendor_credit_memos");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.Total);
        builder.Ignore(e => e.AmountApplied);
        builder.Ignore(e => e.OpenAmount);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.MemoNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Kind).HasColumnName("memo_kind");
        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();
        builder.Property(e => e.VendorReference).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.VoidReason).HasMaxLength(500);
        builder.Property(e => e.TaxAmount).HasPrecision(18, 4);
        builder.Property(e => e.FxRate).HasPrecision(18, 8).HasDefaultValue(1m);

        builder.HasIndex(e => e.MemoNumber).IsUnique().HasDatabaseName("ux_vendor_credit_memos_memo_number");
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.CurrencyId);
        builder.HasIndex(e => e.VendorBillId);
        builder.HasIndex(e => e.VendorReturnId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VendorBill)
            .WithMany()
            .HasForeignKey(e => e.VendorBillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VendorReturn)
            .WithMany()
            .HasForeignKey(e => e.VendorReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.VendorCreditMemo)
            .HasForeignKey(l => l.VendorCreditMemoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Applications)
            .WithOne(a => a.VendorCreditMemo)
            .HasForeignKey(a => a.VendorCreditMemoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

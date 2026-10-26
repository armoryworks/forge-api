using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — credit memos exist in standalone mode; in connected mode they are pushed to the provider.
/// </summary>
public class CreditMemoConfiguration : IEntityTypeConfiguration<CreditMemo>
{
    public void Configure(EntityTypeBuilder<CreditMemo> builder)
    {
        builder.ToTable("credit_memos");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.TaxableSubtotal);
        builder.Ignore(e => e.TaxAmount);
        builder.Ignore(e => e.Total);
        builder.Ignore(e => e.AmountApplied);
        builder.Ignore(e => e.AmountRefunded);
        builder.Ignore(e => e.OpenAmount);
        builder.Ignore(e => e.IsWriteOff);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.CreditMemoNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.VoidReason).HasMaxLength(500);
        builder.Property(e => e.TaxRate).HasPrecision(8, 6);
        builder.Property(e => e.FxRate).HasPrecision(18, 8).HasDefaultValue(1m);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        builder.HasIndex(e => e.CreditMemoNumber).IsUnique().HasDatabaseName("ux_credit_memos_credit_memo_number");
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.CurrencyId);
        builder.HasIndex(e => e.InvoiceId);
        builder.HasIndex(e => e.CustomerReturnId);
        builder.HasIndex(e => e.SalesTaxRateId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Invoice)
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CustomerReturn)
            .WithMany()
            .HasForeignKey(e => e.CustomerReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SalesTaxRate)
            .WithMany()
            .HasForeignKey(e => e.SalesTaxRateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.CreditMemo)
            .HasForeignKey(l => l.CreditMemoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Applications)
            .WithOne(a => a.CreditMemo)
            .HasForeignKey(a => a.CreditMemoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Refunds)
            .WithOne(r => r.CreditMemo)
            .HasForeignKey(r => r.CreditMemoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

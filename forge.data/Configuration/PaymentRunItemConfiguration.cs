using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentRunItemConfiguration : IEntityTypeConfiguration<PaymentRunItem>
{
    public void Configure(EntityTypeBuilder<PaymentRunItem> builder)
    {
        builder.ToTable("payment_run_items");

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.DiscountAmount).HasPrecision(18, 2);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(e => new { e.PaymentRunId, e.VendorBillId }).IsUnique()
            .HasDatabaseName("ux_payment_run_items_payment_run_id_vendor_bill_id");
        builder.HasIndex(e => e.VendorBillId);
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.CurrencyId);
        builder.HasIndex(e => e.VendorPaymentId);

        builder.HasOne(e => e.VendorBill)
            .WithMany()
            .HasForeignKey(e => e.VendorBillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VendorPayment>()
            .WithMany()
            .HasForeignKey(e => e.VendorPaymentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Credits)
            .WithOne(c => c.PaymentRunItem)
            .HasForeignKey(c => c.PaymentRunItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

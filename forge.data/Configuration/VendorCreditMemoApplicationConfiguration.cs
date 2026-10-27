using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorCreditMemoApplicationConfiguration : IEntityTypeConfiguration<VendorCreditMemoApplication>
{
    public void Configure(EntityTypeBuilder<VendorCreditMemoApplication> builder)
    {
        builder.ToTable("vendor_credit_memo_applications");

        builder.Ignore(e => e.IsLive);

        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => e.VendorCreditMemoId);
        builder.HasIndex(e => e.VendorBillId);
        builder.HasIndex(e => e.VendorPaymentId);
        builder.HasIndex(e => e.PaymentRunId);

        builder.HasOne(e => e.VendorBill)
            .WithMany(b => b.CreditApplications)
            .HasForeignKey(e => e.VendorBillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VendorPayment>()
            .WithMany(p => p.CreditApplications)
            .HasForeignKey(e => e.VendorPaymentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PaymentRun>()
            .WithMany()
            .HasForeignKey(e => e.PaymentRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

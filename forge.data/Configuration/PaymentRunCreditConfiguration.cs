using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PaymentRunCreditConfiguration : IEntityTypeConfiguration<PaymentRunCredit>
{
    public void Configure(EntityTypeBuilder<PaymentRunCredit> builder)
    {
        builder.ToTable("payment_run_credits");

        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => e.PaymentRunId);
        builder.HasIndex(e => e.PaymentRunItemId);
        builder.HasIndex(e => e.VendorCreditMemoId);
        builder.HasIndex(e => e.VendorCreditMemoApplicationId);

        builder.HasOne(e => e.VendorCreditMemo)
            .WithMany()
            .HasForeignKey(e => e.VendorCreditMemoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VendorCreditMemoApplication>()
            .WithMany()
            .HasForeignKey(e => e.VendorCreditMemoApplicationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

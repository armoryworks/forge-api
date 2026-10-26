using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>⚡ ACCOUNTING BOUNDARY — customer refunds exist in standalone mode.</summary>
public class CustomerRefundConfiguration : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> builder)
    {
        builder.ToTable("customer_refunds");

        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.RefundNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.FxRate).HasPrecision(18, 8).HasDefaultValue(1m);
        builder.Property(e => e.Reference).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.VoidReason).HasMaxLength(500);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        builder.HasIndex(e => e.RefundNumber).IsUnique().HasDatabaseName("ux_customer_refunds_refund_number");
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.CurrencyId);
        builder.HasIndex(e => e.CreditMemoId);
        builder.HasIndex(e => e.PaymentId);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

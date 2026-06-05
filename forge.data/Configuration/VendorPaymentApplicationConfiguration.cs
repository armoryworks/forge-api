using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — VendorPaymentApplication. Both relationships (→VendorPayment, →VendorBill)
/// are defined on the parent configs; this sets the table, money precision, and the lookup indexes.
/// </summary>
public class VendorPaymentApplicationConfiguration : IEntityTypeConfiguration<VendorPaymentApplication>
{
    public void Configure(EntityTypeBuilder<VendorPaymentApplication> builder)
    {
        builder.ToTable("vendor_payment_applications");

        builder.Property(e => e.Amount).HasPrecision(18, 4);

        builder.HasIndex(e => e.VendorPaymentId).HasDatabaseName("ix_vpa_payment");
        builder.HasIndex(e => e.VendorBillId).HasDatabaseName("ix_vpa_bill");
    }
}

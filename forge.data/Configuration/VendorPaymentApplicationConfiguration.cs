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
        // Settlement FX rate (Phase-4 FULLGL, additive) — mirrors PaymentApplication. Default 1 so existing
        // rows backfill to unity (single-currency settlement path unchanged). Precision matches JournalLine.FxRate.
        builder.Property(e => e.SettlementFxRate).HasPrecision(18, 8).HasDefaultValue(1m);

        builder.HasIndex(e => e.VendorPaymentId).HasDatabaseName("ix_vpa_payment");
        builder.HasIndex(e => e.VendorBillId).HasDatabaseName("ix_vpa_bill");
    }
}

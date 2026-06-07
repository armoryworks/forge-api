using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — VendorBill (AP) entity, Phase-2 AP sub-ledger. Mirrors
/// <see cref="InvoiceConfiguration"/>: optimistic-locking <c>Version</c>, computed money columns
/// ignored, explicit index/constraint names.
/// </summary>
public class VendorBillConfiguration : IEntityTypeConfiguration<VendorBill>
{
    public void Configure(EntityTypeBuilder<VendorBill> builder)
    {
        builder.ToTable("vendor_bills");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.Total);
        builder.Ignore(e => e.AmountPaid);
        builder.Ignore(e => e.BalanceDue);

        // Optimistic locking — WHERE version=@orig on UPDATE (mirrors Invoice — WU-11 / F-026).
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.BillNumber).HasMaxLength(30).IsRequired();
        builder.Property(e => e.VendorInvoiceNumber).HasMaxLength(60);
        builder.Property(e => e.TaxAmount).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        builder.HasIndex(e => e.BillNumber).IsUnique().HasDatabaseName("ux_vendor_bills_number");
        // Pre-go-live AP control (hardening): block paying the same vendor invoice twice. Partial unique on
        // (vendor, vendor_invoice_number) where a number is present — bills without a vendor invoice number
        // (manual / not-yet-keyed) are exempt. The handler also guards with a friendly 4xx before this fires.
        builder.HasIndex(e => new { e.VendorId, e.VendorInvoiceNumber })
            .IsUnique()
            .HasDatabaseName("ux_vendor_bills_vendor_invoice")
            .HasFilter("vendor_invoice_number IS NOT NULL");
        builder.HasIndex(e => e.VendorId).HasDatabaseName("ix_vendor_bills_vendor");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_vendor_bills_status");
        builder.HasIndex(e => e.PurchaseOrderId).HasDatabaseName("ix_vendor_bills_po");

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .HasConstraintName("fk_vendor_bills_vendor")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderId)
            .HasConstraintName("fk_vendor_bills_po")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.VendorBill)
            .HasForeignKey(l => l.VendorBillId)
            .HasConstraintName("fk_vendor_bill_lines_bill")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.PaymentApplications)
            .WithOne(pa => pa.VendorBill)
            .HasForeignKey(pa => pa.VendorBillId)
            .HasConstraintName("fk_vpa_bill")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

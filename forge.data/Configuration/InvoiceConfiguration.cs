using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Invoice entity exists in standalone mode; read-only cache in integrated mode.
/// </summary>
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.TaxAmount);
        builder.Ignore(e => e.Total);
        builder.Ignore(e => e.AmountPaid);
        builder.Ignore(e => e.BalanceDue);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.Property(e => e.InvoiceNumber).HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.TaxRate).HasPrecision(8, 6);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        // Customer PO # echoed from SO at invoice creation. Same 50-char cap
        // as SalesOrder.CustomerPO so values round-trip without truncation.
        builder.Property(e => e.CustomerPO).HasMaxLength(50);

        builder.HasIndex(e => e.InvoiceNumber).IsUnique();
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.ShipmentId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SalesOrder)
            .WithMany(so => so.Invoices)
            .HasForeignKey(e => e.SalesOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Shipment)
            .WithOne(s => s.Invoice)
            .HasForeignKey<Invoice>(e => e.ShipmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.PaymentApplications)
            .WithOne(pa => pa.Invoice)
            .HasForeignKey(pa => pa.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

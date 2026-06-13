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

        // WU-11 / F-026 — optimistic locking; token causes WHERE version=@orig on UPDATE
        // so concurrent SaveChanges on the same invoice throws DbUpdateConcurrencyException.
        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.InvoiceNumber).HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.TaxRate).HasPrecision(8, 6);

        // Multi-currency (Phase-4 FULLGL, additive). Column default 1 so existing rows backfill to the
        // functional currency / unity rate — the single-currency path stays byte-for-byte unchanged.
        // FxRate precision (18,8) matches JournalLine.FxRate / ExchangeRate.Rate (§5.6).
        builder.Property(e => e.CurrencyId).HasDefaultValue(1);
        builder.Property(e => e.FxRate).HasPrecision(18, 8).HasDefaultValue(1m);
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

        // FK to currencies (mirrors Book.FunctionalCurrency — Restrict, no inverse nav).
        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CurrencyId);

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

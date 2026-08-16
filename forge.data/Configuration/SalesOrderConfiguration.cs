using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Data.Configuration;

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Subtotal);
        builder.Ignore(e => e.TaxAmount);
        builder.Ignore(e => e.Total);
        builder.Ignore(e => e.SellerTaxLiability);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.Property(e => e.OrderNumber).HasMaxLength(20);
        builder.Property(e => e.CustomerPO).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.TaxRate).HasPrecision(8, 6);
        builder.Property(e => e.CancellationFeeAmount).HasPrecision(18, 2);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.ExternalOrderNumber).HasMaxLength(100);
        // Default declared at the database level so the column could be added
        // NOT NULL to an existing sales_orders table without a backfill pass —
        // every pre-channel order is seller-collected by definition.
        builder.Property(e => e.TaxCollectedBy)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(TaxCollectedBy.Seller);

        builder.HasIndex(e => e.OrderNumber).IsUnique();
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.QuoteId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ChannelId);
        builder.HasIndex(e => e.RetailBuyerId);

        // Customer-service lookup path: "the buyer is quoting me their Amazon
        // order number". Scoped by channel because external numbers are only
        // unique within a marketplace.
        builder.HasIndex(e => new { e.ChannelId, e.ExternalOrderNumber })
            .HasFilter("external_order_number IS NOT NULL");

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.SalesOrders)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Quote)
            .WithOne(q => q.SalesOrder)
            .HasForeignKey<SalesOrder>(e => e.QuoteId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict on both: a channel or buyer with orders against it must be
        // deactivated, not deleted, or the order loses the context that
        // explains why it skipped credit and quoting.
        builder.HasOne(e => e.Channel)
            .WithMany(c => c.SalesOrders)
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RetailBuyer)
            .WithMany(b => b.SalesOrders)
            .HasForeignKey(e => e.RetailBuyerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Explicit, or EF pairs this with Attestation.SalesOrder and mints a
        // shadow authorizing_attestation_id1 column alongside the real one.
        // Restrict: the authorizing statement is the order's proof of intent —
        // it must never be silently detachable.
        builder.HasOne(e => e.AuthorizingAttestation)
            .WithMany()
            .HasForeignKey(e => e.AuthorizingAttestationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ShippingAddress)
            .WithMany()
            .HasForeignKey(e => e.ShippingAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.BillingAddress)
            .WithMany()
            .HasForeignKey(e => e.BillingAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.SalesOrder)
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Shipments)
            .WithOne(s => s.SalesOrder)
            .HasForeignKey(s => s.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

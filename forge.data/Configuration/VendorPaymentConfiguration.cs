using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — VendorPayment (AP) entity, Phase-2 AP sub-ledger. Mirrors the
/// Payment configuration: optimistic-locking <c>Version</c>, computed amounts ignored, explicit names.
/// </summary>
public class VendorPaymentConfiguration : IEntityTypeConfiguration<VendorPayment>
{
    public void Configure(EntityTypeBuilder<VendorPayment> builder)
    {
        builder.ToTable("vendor_payments");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.AppliedAmount);
        builder.Ignore(e => e.UnappliedAmount);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.PaymentNumber).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 4);
        builder.Property(e => e.ReferenceNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);

        builder.HasIndex(e => e.PaymentNumber).IsUnique().HasDatabaseName("ux_vendor_payments_number");
        builder.HasIndex(e => e.VendorId).HasDatabaseName("ix_vendor_payments_vendor");

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .HasConstraintName("fk_vendor_payments_vendor")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Applications)
            .WithOne(a => a.VendorPayment)
            .HasForeignKey(a => a.VendorPaymentId)
            .HasConstraintName("fk_vpa_payment")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

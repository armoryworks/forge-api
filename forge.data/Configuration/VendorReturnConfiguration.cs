using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorReturnConfiguration : IEntityTypeConfiguration<VendorReturn>
{
    public void Configure(EntityTypeBuilder<VendorReturn> builder)
    {
        builder.ToTable("vendor_returns");

        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.ExpectedCredit);

        builder.Property(e => e.Version).HasDefaultValue(1u).IsConcurrencyToken();

        builder.Property(e => e.ReturnNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Carrier).HasMaxLength(100);
        builder.Property(e => e.TrackingNumber).HasMaxLength(100);
        builder.Property(e => e.RmaNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.FxRate).HasPrecision(18, 8).HasDefaultValue(1m);

        builder.HasIndex(e => e.ReturnNumber).IsUnique().HasDatabaseName("ux_vendor_returns_return_number");
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.NonConformanceId);
        builder.HasIndex(e => e.PurchaseOrderId);
        builder.HasIndex(e => e.PurchaseOrderLineId);
        builder.HasIndex(e => e.ReceivingRecordId);
        builder.HasIndex(e => e.CurrencyId);
        builder.HasIndex(e => e.Status);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NonConformance)
            .WithMany()
            .HasForeignKey(e => e.NonConformanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PurchaseOrder)
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(e => e.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ReceivingRecord>()
            .WithMany()
            .HasForeignKey(e => e.ReceivingRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Currency)
            .WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.VendorReturn)
            .HasForeignKey(l => l.VendorReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

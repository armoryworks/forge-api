using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.SignatureRequired).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Ignore(e => e.IsDeleted);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.Property(e => e.ShipmentNumber).HasMaxLength(20);
        builder.Property(e => e.Carrier).HasMaxLength(100);
        builder.Property(e => e.ScanCode).HasMaxLength(120);
        builder.Property(e => e.TrackingNumber).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.ShippingCost).HasPrecision(18, 4);
        builder.Property(e => e.Weight).HasPrecision(12, 4);
        builder.Property(e => e.ServiceType).HasMaxLength(200);
        builder.Property(e => e.FreightClass).HasMaxLength(50);
        builder.Property(e => e.InsuredValue).HasPrecision(18, 4);
        builder.Property(e => e.BillOfLadingNumber).HasMaxLength(200);

        builder.HasIndex(e => e.ShipmentNumber).IsUnique();
        builder.HasIndex(e => e.SalesOrderId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CarrierId);

        builder.HasOne(e => e.ShippingAddress)
            .WithMany()
            .HasForeignKey(e => e.ShippingAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        // Distinct nav name (AssignedCarrier) so the FK doesn't collide with the legacy free-text
        // `Carrier` string. SetNull on carrier delete — the shipment keeps its tracking/free-text.
        builder.HasOne(e => e.AssignedCarrier)
            .WithMany()
            .HasForeignKey(e => e.CarrierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.Shipment)
            .HasForeignKey(l => l.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

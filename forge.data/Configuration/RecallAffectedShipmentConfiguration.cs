using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RecallAffectedShipmentConfiguration : IEntityTypeConfiguration<RecallAffectedShipment>
{
    public void Configure(EntityTypeBuilder<RecallAffectedShipment> builder)
    {
        builder.Property(e => e.AffectedQuantity).HasPrecision(18, 4);
        builder.Property(e => e.TrackingNumber).HasMaxLength(200);

        builder.HasOne(e => e.Shipment)
            .WithMany()
            .HasForeignKey(e => e.ShipmentId)
            .HasConstraintName("fk_recall_affected_shipments__shipments_shipment_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .HasConstraintName("fk_recall_affected_shipments__customers_customer_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

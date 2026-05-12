using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ShipmentPackageConfiguration : IEntityTypeConfiguration<ShipmentPackage>
{
    public void Configure(EntityTypeBuilder<ShipmentPackage> builder)
    {
        builder.Property(p => p.TrackingNumber).HasMaxLength(200);
        builder.Property(p => p.Carrier).HasMaxLength(100);
        builder.Property(p => p.Status).HasMaxLength(50);
        builder.Property(p => p.Weight).HasPrecision(10, 2);
        builder.Property(p => p.Length).HasPrecision(10, 2);
        builder.Property(p => p.Width).HasPrecision(10, 2);
        builder.Property(p => p.Height).HasPrecision(10, 2);

        builder.HasOne(p => p.Shipment)
            .WithMany(s => s.Packages)
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.ShipmentId);
    }
}

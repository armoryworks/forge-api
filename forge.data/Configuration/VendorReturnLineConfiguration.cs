using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorReturnLineConfiguration : IEntityTypeConfiguration<VendorReturnLine>
{
    public void Configure(EntityTypeBuilder<VendorReturnLine> builder)
    {
        builder.ToTable("vendor_return_lines");

        builder.Property(e => e.LotNumber).HasMaxLength(100);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.UnitCost).HasPrecision(18, 6);
        builder.Property(e => e.ShippedCost).HasPrecision(18, 4);

        builder.HasIndex(e => e.VendorReturnId);
        builder.HasIndex(e => e.PartId);
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.BinContentId);
        builder.HasIndex(e => e.BinMovementId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<BinContent>()
            .WithMany()
            .HasForeignKey(e => e.BinContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<BinMovement>()
            .WithMany()
            .HasForeignKey(e => e.BinMovementId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

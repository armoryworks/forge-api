using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorPartPriceTierConfiguration : IEntityTypeConfiguration<VendorPartPriceTier>
{
    public void Configure(EntityTypeBuilder<VendorPartPriceTier> builder)
    {
        // Lookup by vendor_part + min_qty is the hot path (price for a given
        // requested qty). Effective-from is also frequently filtered.
        builder.HasIndex(e => new { e.VendorPartId, e.MinQuantity });
        builder.HasIndex(e => new { e.VendorPartId, e.EffectiveFrom });

        builder.Property(e => e.MinQuantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);

        // UoM purchase-units effort — which size/form this tier prices (null = single default).
        builder.HasIndex(e => e.PurchaseUnitId);
        builder.HasOne(e => e.PurchaseUnit)
            .WithMany()
            .HasForeignKey(e => e.PurchaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

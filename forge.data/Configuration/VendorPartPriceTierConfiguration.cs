using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorPartPriceTierConfiguration : IEntityTypeConfiguration<VendorPartPriceTier>
{
    public void Configure(EntityTypeBuilder<VendorPartPriceTier> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.FreightIncluded).HasDefaultValueSql("false");
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

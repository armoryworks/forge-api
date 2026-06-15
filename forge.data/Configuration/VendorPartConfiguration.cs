using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorPartConfiguration : IEntityTypeConfiguration<VendorPart>
{
    public void Configure(EntityTypeBuilder<VendorPart> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.Incoterm).HasDefaultValueSql("0");
        builder.Ignore(e => e.IsDeleted);

        // (vendor, part) is unique — at most one VendorPart per vendor-part
        // combination. Multi-source-from-same-vendor (e.g., the same vendor
        // sells the part in two SKUs) is rare; if needed, model as a vendor
        // alternate part on a separate row.
        builder.HasIndex(e => new { e.VendorId, e.PartId }).IsUnique();
        builder.HasIndex(e => e.PartId);

        builder.Property(e => e.VendorPartNumber).HasMaxLength(100);
        builder.Property(e => e.ManufacturerName).HasMaxLength(200);
        builder.Property(e => e.VendorMpn).HasMaxLength(100);
        builder.HasIndex(e => e.VendorMpn);
        builder.Property(e => e.CountryOfOrigin).HasMaxLength(2);
        builder.Property(e => e.HtsCode).HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Certifications).HasColumnType("jsonb");
        builder.Property(e => e.Currency).HasMaxLength(3).HasDefaultValue("USD").IsRequired();

        // Direct-from-MFR flag. Default false so distributor-source rows
        // (the common case) don't get tagged "from manufacturer" implicitly
        // when the column was added.
        builder.Property(e => e.IsManufacturer).HasDefaultValue(false);

        builder.Property(e => e.MinOrderQty).HasPrecision(18, 4);
        builder.Property(e => e.PackSize).HasPrecision(18, 4);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.PriceTiers)
            .WithOne(t => t.VendorPart)
            .HasForeignKey(t => t.VendorPartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

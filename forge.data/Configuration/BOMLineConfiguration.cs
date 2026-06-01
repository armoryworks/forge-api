using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BOMLineConfiguration : IEntityTypeConfiguration<BOMLine>
{
    public void Configure(EntityTypeBuilder<BOMLine> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.ReferenceDesignator).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasOne(e => e.ParentPart)
            .WithMany(p => p.BOMLines)
            .HasForeignKey(e => e.ParentPartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ChildPart)
            .WithMany(p => p.UsedInBOM)
            .HasForeignKey(e => e.ChildPartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.VendorId)
            .HasFilter("vendor_id IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorAddressConfiguration : IEntityTypeConfiguration<VendorAddress>
{
    public void Configure(EntityTypeBuilder<VendorAddress> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.Line1).HasMaxLength(200);
        builder.Property(e => e.Line2).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(50);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(10);

        builder.HasIndex(e => e.VendorId);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

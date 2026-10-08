using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class VendorContactConfiguration : IEntityTypeConfiguration<VendorContact>
{
    public void Configure(EntityTypeBuilder<VendorContact> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.FirstName).HasMaxLength(100);
        builder.Property(e => e.LastName).HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Mobile).HasMaxLength(50);
        builder.Property(e => e.Fax).HasMaxLength(50);
        builder.Property(e => e.Role).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.VendorId);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

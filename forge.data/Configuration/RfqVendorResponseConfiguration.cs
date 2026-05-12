using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RfqVendorResponseConfiguration : IEntityTypeConfiguration<RfqVendorResponse>
{
    public void Configure(EntityTypeBuilder<RfqVendorResponse> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.MinimumOrderQuantity).HasPrecision(18, 4);
        builder.Property(e => e.ToolingCost).HasPrecision(18, 4);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.DeclineReason).HasMaxLength(500);

        builder.HasIndex(e => new { e.RfqId, e.VendorId }).IsUnique();

        builder.HasOne(e => e.Rfq)
            .WithMany(r => r.VendorResponses)
            .HasForeignKey(e => e.RfqId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Vendor)
            .WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

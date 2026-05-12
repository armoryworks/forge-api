using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class RequestForQuoteConfiguration : IEntityTypeConfiguration<RequestForQuote>
{
    public void Configure(EntityTypeBuilder<RequestForQuote> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.RfqNumber).HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.SpecialInstructions).HasMaxLength(4000);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        builder.HasIndex(e => e.RfqNumber).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.VendorResponses)
            .WithOne(r => r.Rfq)
            .HasForeignKey(r => r.RfqId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

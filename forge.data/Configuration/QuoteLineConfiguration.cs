using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.Ignore(e => e.LineTotal);

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        // Phase 3 / WU-23 (F8-broad): decimal(18,4) for UoM-aware fractional qty.
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.QuoteId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

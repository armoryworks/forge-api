using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TariffRateConfiguration : IEntityTypeConfiguration<TariffRate>
{
    public void Configure(EntityTypeBuilder<TariffRate> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.HtsCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.CountryOfOrigin).HasMaxLength(2).IsRequired();
        builder.Property(e => e.RatePct).HasPrecision(8, 4);
        builder.Property(e => e.Source).HasMaxLength(200);

        // Resolution path: WHERE hts_code = X AND country_of_origin = Y
        // AND effective_from <= receipt_date AND (effective_to IS NULL OR
        // effective_to > receipt_date). This index covers the lookup.
        builder.HasIndex(e => new { e.HtsCode, e.CountryOfOrigin, e.EffectiveFrom });
    }
}

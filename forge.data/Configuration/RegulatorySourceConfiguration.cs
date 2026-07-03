using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Regulatory;

namespace Forge.Data.Configuration;

public class RegulatorySourceConfiguration : IEntityTypeConfiguration<RegulatorySource>
{
    public void Configure(EntityTypeBuilder<RegulatorySource> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.IssuingBody).HasMaxLength(200);
        builder.Property(e => e.Domain).HasMaxLength(60);
        builder.Property(e => e.Url).HasMaxLength(1000);
        builder.Property(e => e.FeedType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.IndustryGate).HasMaxLength(40);

        builder.HasIndex(e => e.IsActive).HasDatabaseName("ix_regulatory_sources_is_active");
    }
}

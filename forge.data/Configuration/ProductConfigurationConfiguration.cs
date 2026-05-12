using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ProductConfigurationConfiguration : IEntityTypeConfiguration<ProductConfiguration>
{
    public void Configure(EntityTypeBuilder<ProductConfiguration> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.ConfigurationCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ComputedPrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.ConfiguratorId);
        builder.HasIndex(e => e.ConfigurationCode).IsUnique();
        builder.HasIndex(e => e.QuoteId);
        builder.HasIndex(e => e.PartId);

        builder.HasOne(e => e.Quote)
            .WithMany()
            .HasForeignKey(e => e.QuoteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

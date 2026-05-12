using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ProductConfiguratorConfiguration : IEntityTypeConfiguration<ProductConfigurator>
{
    public void Configure(EntityTypeBuilder<ProductConfigurator> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.BasePrice).HasPrecision(18, 4);

        builder.HasIndex(e => e.BasePartId);
        builder.HasIndex(e => e.IsActive);

        builder.HasOne(e => e.BasePart)
            .WithMany()
            .HasForeignKey(e => e.BasePartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Options)
            .WithOne(o => o.Configurator)
            .HasForeignKey(o => o.ConfiguratorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Configurations)
            .WithOne(c => c.Configurator)
            .HasForeignKey(c => c.ConfiguratorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

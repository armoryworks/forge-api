using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ConfiguratorOptionConfiguration : IEntityTypeConfiguration<ConfiguratorOption>
{
    public void Configure(EntityTypeBuilder<ConfiguratorOption> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DependsOnOptionId).HasMaxLength(50);
        builder.Property(e => e.HelpText).HasMaxLength(500);
        builder.Property(e => e.DefaultValue).HasMaxLength(500);

        builder.HasIndex(e => e.ConfiguratorId);
    }
}

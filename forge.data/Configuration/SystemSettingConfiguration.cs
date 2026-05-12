using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasIndex(e => e.Key).IsUnique();

        builder.Property(e => e.Key).HasMaxLength(100);
        builder.Property(e => e.Value).HasMaxLength(2000);
        builder.Property(e => e.Description).HasMaxLength(500);
    }
}

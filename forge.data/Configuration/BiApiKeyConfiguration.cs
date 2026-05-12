using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BiApiKeyConfiguration : IEntityTypeConfiguration<BiApiKey>
{
    public void Configure(EntityTypeBuilder<BiApiKey> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.KeyHash).HasMaxLength(500).IsRequired();
        builder.Property(e => e.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(e => e.AllowedEntitySetsJson).HasColumnType("jsonb");
        builder.Property(e => e.AllowedIpsJson).HasColumnType("jsonb");

        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.KeyPrefix);
    }
}

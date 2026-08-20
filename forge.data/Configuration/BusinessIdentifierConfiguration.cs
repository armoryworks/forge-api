using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class BusinessIdentifierConfiguration : IEntityTypeConfiguration<BusinessIdentifier>
{
    public void Configure(EntityTypeBuilder<BusinessIdentifier> builder)
    {
        builder.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Value).HasMaxLength(80);
        // Mirrors the forge-db GENERATED ALWAYS AS (effective_to IS NULL) STORED column — read-only.
        builder.Property(e => e.IsActive).HasComputedColumnSql("effective_to IS NULL", stored: true);

        builder.HasIndex(e => e.Value);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}

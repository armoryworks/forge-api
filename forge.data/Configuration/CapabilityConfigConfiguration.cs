using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CapabilityConfigConfiguration : IEntityTypeConfiguration<CapabilityConfig>
{
    public void Configure(EntityTypeBuilder<CapabilityConfig> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.CapabilityId).IsRequired();
        builder.HasIndex(e => e.CapabilityId).IsUnique(); // 1:0..1 enforced

        builder.Property(e => e.ConfigJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.SchemaVersion).IsRequired();

        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Phase 4 Phase-C — API-surfaced ETag value (Capability.Version-style).
        builder.Property(e => e.Version).HasDefaultValue(1u);
    }
}

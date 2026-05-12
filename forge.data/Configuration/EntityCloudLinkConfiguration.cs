using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EntityCloudLinkConfiguration : IEntityTypeConfiguration<EntityCloudLink>
{
    public void Configure(EntityTypeBuilder<EntityCloudLink> builder)
    {
        builder.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FolderExternalId).HasMaxLength(500).IsRequired();
        builder.Property(e => e.FolderPath).HasMaxLength(2000);
        builder.Property(e => e.FolderUrl).HasMaxLength(2000);
        builder.Property(e => e.CreatedVia).HasMaxLength(50).IsRequired();

        // One link per (entity type, entity id, provider). Hybrid storage
        // is supported because the unique key includes provider — one
        // entity can in principle link to folders on multiple providers,
        // though that's a tier-2 use case.
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.ProviderId })
            .HasFilter("deleted_at IS NULL")
            .IsUnique();

        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.ProviderId);
    }
}

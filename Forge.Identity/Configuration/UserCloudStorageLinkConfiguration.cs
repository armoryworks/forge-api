using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

public class UserCloudStorageLinkConfiguration : IEntityTypeConfiguration<UserCloudStorageLink>
{
    public void Configure(EntityTypeBuilder<UserCloudStorageLink> builder)
    {
        builder.Property(e => e.ExternalUserId).HasMaxLength(500);
        builder.Property(e => e.OAuthTokenEncrypted).HasMaxLength(4000).IsRequired();
        builder.Property(e => e.RefreshTokenEncrypted).HasMaxLength(4000).IsRequired();

        // One link per (user, provider).
        builder.HasIndex(e => new { e.UserId, e.ProviderId })
            .HasFilter("deleted_at IS NULL")
            .IsUnique();
    }
}

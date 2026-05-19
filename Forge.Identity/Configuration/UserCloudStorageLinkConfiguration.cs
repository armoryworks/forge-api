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

        // FK to ApplicationUser. Cascade DELETE — when a user is hard-
        // deleted, their per-user OAuth grants go with them. Forge typically
        // soft-deactivates users (IsActive=false) rather than hard-deleting,
        // so cascade is the right default for the rare hard-delete path.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            // Pin pre-move constraint name (assembly relocation shifted EF's
            // default FK-name generation — keep the live DB constraint stable).
            .HasConstraintName("fk_user_cloud_storage_links__asp_net_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // One link per (user, provider).
        builder.HasIndex(e => new { e.UserId, e.ProviderId })
            .HasFilter("deleted_at IS NULL")
            .IsUnique();
    }
}

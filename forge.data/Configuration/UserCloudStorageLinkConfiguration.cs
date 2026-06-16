using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class UserCloudStorageLinkConfiguration : IEntityTypeConfiguration<UserCloudStorageLink>
{
    public void Configure(EntityTypeBuilder<UserCloudStorageLink> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.UserId).HasDefaultValueSql("0").ValueGeneratedNever();
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
            .OnDelete(DeleteBehavior.Cascade);

        // One link per (user, provider).
        builder.HasIndex(e => new { e.UserId, e.ProviderId })
            .HasFilter("deleted_at IS NULL")
            .IsUnique();
    }
}

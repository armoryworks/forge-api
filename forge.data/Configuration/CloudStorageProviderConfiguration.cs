using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CloudStorageProviderConfiguration : IEntityTypeConfiguration<CloudStorageProvider>
{
    public void Configure(EntityTypeBuilder<CloudStorageProvider> builder)
    {
        builder.Property(e => e.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.RootFolderId).HasMaxLength(500);
        builder.Property(e => e.ServiceAccountId).HasMaxLength(500);
        builder.Property(e => e.OAuthTokenEncrypted).HasMaxLength(4000);
        builder.Property(e => e.RefreshTokenEncrypted).HasMaxLength(4000);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.Settings).HasColumnType("jsonb");

        // One provider per (ProviderCode) where IsActive=true. Hybrid storage
        // can hold multiple inactive history rows; only one is active per
        // provider code at a time.
        builder.HasIndex(e => new { e.ProviderCode, e.IsActive })
            .HasFilter("is_active = true AND deleted_at IS NULL")
            .IsUnique();

        builder.HasMany(e => e.UserLinks)
            .WithOne(l => l.Provider)
            .HasForeignKey(l => l.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.EntityLinks)
            .WithOne(l => l.Provider)
            .HasForeignKey(l => l.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

/// <summary>
/// Wave 8 phase 1k.2 — EF config for <see cref="OAuthStateToken"/>.
/// Indexes Token alone (the lookup key on callback) and adds a unique
/// constraint so Token collisions can't accidentally route a callback
/// to the wrong row.
/// </summary>
public class OAuthStateTokenConfiguration : IEntityTypeConfiguration<OAuthStateToken>
{
    public void Configure(EntityTypeBuilder<OAuthStateToken> builder)
    {
        builder.Property(e => e.Token).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ProviderKey).HasMaxLength(32).IsRequired();

        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasIndex(e => e.UserId);
    }
}

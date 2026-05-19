using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Configuration;

public class SystemApiKeyConfiguration : IEntityTypeConfiguration<SystemApiKey>
{
    public void Configure(EntityTypeBuilder<SystemApiKey> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.KeyHash).HasMaxLength(500).IsRequired();
        builder.Property(e => e.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ScopesJson).HasColumnType("jsonb");
        builder.Property(e => e.AllowedIpsJson).HasColumnType("jsonb");

        // FK to ApplicationUser. RESTRICT prevents accidental loss of an
        // active integration key when an admin tries to hard-delete the
        // bound user (which would also be unusual — service users are
        // soft-deactivated via IsActive=false, not removed). Cascading
        // would silently invalidate live integrations.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.IsActive);
        builder.HasIndex(e => e.KeyPrefix);
    }
}

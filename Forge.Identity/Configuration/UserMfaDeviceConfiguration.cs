using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Identity.Configuration;

public class UserMfaDeviceConfiguration : IEntityTypeConfiguration<UserMfaDevice>
{
    public void Configure(EntityTypeBuilder<UserMfaDevice> builder)
    {
        builder.Property(d => d.DeviceType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.EncryptedSecret).HasMaxLength(500);
        builder.Property(d => d.DeviceName).HasMaxLength(100);
        builder.Property(d => d.CredentialId).HasMaxLength(500);
        builder.Property(d => d.PublicKey).HasMaxLength(2000);
        builder.Property(d => d.PhoneNumber).HasMaxLength(20);
        builder.Property(d => d.EmailAddress).HasMaxLength(256);

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => new { d.UserId, d.IsDefault })
            .HasFilter("is_default = true AND deleted_at IS NULL")
            .IsUnique();
    }
}

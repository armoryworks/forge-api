using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

public class UserScanDeviceConfiguration : IEntityTypeConfiguration<UserScanDevice>
{
    public void Configure(EntityTypeBuilder<UserScanDevice> builder)
    {
        builder.Property(e => e.DeviceId).HasMaxLength(200);
        builder.Property(e => e.DeviceName).HasMaxLength(200);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.DeviceId).IsUnique();
        builder.HasIndex(e => e.UserId);
    }
}

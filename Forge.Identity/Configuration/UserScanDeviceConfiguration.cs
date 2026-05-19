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
            // Pin pre-move constraint name (assembly relocation shifted EF's
            // default FK-name generation — keep the live DB constraint stable).
            .HasConstraintName("fk_user_scan_devices__asp_net_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.DeviceId).IsUnique();
        builder.HasIndex(e => e.UserId);
    }
}

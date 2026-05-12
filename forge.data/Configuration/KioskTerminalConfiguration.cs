using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class KioskTerminalConfiguration : IEntityTypeConfiguration<KioskTerminal>
{
    public void Configure(EntityTypeBuilder<KioskTerminal> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).HasMaxLength(100);
        builder.Property(e => e.DeviceToken).HasMaxLength(100);

        builder.HasIndex(e => e.DeviceToken).IsUnique();
        builder.HasIndex(e => e.TeamId);
        builder.HasIndex(e => e.WorkCenterId);

        builder.HasOne(e => e.Team)
            .WithMany()
            .HasForeignKey(e => e.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: a retired work center shouldn't break the kiosk pairing.
        // The terminal falls back to team-wide context until reassigned.
        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

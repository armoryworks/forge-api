using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Identity.Configuration;

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

        // Pin the pre-move FK constraint names. Relocating these configs to the
        // Forge.Identity assembly shifted EF's default FK-name generation
        // (double- vs single-underscore); a code move must not rename live DB
        // constraints, so the existing names are pinned explicitly.
        builder.HasOne(e => e.Team)
            .WithMany()
            .HasForeignKey(e => e.TeamId)
            .HasConstraintName("fk_kiosk_terminals__teams_team_id")
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: a retired work center shouldn't break the kiosk pairing.
        // The terminal falls back to team-wide context until reassigned.
        builder.HasOne(e => e.WorkCenter)
            .WithMany()
            .HasForeignKey(e => e.WorkCenterId)
            .HasConstraintName("fk_kiosk_terminals__work_centers_work_center_id")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

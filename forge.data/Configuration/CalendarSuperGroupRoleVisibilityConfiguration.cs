using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Calendar;

namespace Forge.Data.Configuration;

public class CalendarSuperGroupRoleVisibilityConfiguration : IEntityTypeConfiguration<CalendarSuperGroupRoleVisibility>
{
    public void Configure(EntityTypeBuilder<CalendarSuperGroupRoleVisibility> builder)
    {
        builder.Property(e => e.Role).HasMaxLength(60);

        // One grant per (group, role). Leading super_group_id also serves the FK lookup.
        builder.HasIndex(e => new { e.SuperGroupId, e.Role })
            .IsUnique()
            .HasDatabaseName("ix_calendar_super_group_role_visibilities_group_role");

        builder.HasOne(e => e.SuperGroup)
            .WithMany()
            .HasForeignKey(e => e.SuperGroupId)
            .HasConstraintName("fk_calendar_super_group_role_visibilities__calendar_super_groups_super_group_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Calendar;

namespace Forge.Data.Configuration;

public class CalendarEventTypeConfiguration : IEntityTypeConfiguration<CalendarEventType>
{
    public void Configure(EntityTypeBuilder<CalendarEventType> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(80);
        builder.Property(e => e.Name).HasMaxLength(120);
        builder.Property(e => e.Color).HasMaxLength(20);

        builder.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_calendar_event_types_key");
        builder.HasIndex(e => e.SuperGroupId).HasDatabaseName("ix_calendar_event_types_super_group_id");

        builder.HasOne(e => e.SuperGroup)
            .WithMany(g => g.EventTypes)
            .HasForeignKey(e => e.SuperGroupId)
            .HasConstraintName("fk_calendar_event_types__calendar_super_groups_super_group_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Calendar;

namespace Forge.Data.Configuration;

public class CalendarSavedViewConfiguration : IEntityTypeConfiguration<CalendarSavedView>
{
    public void Configure(EntityTypeBuilder<CalendarSavedView> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(120);
        builder.Property(e => e.RoleKey).HasMaxLength(60);
        builder.Property(e => e.Scope).HasMaxLength(60);

        builder.HasIndex(e => e.OwnerUserId).HasDatabaseName("ix_calendar_saved_views_owner_user_id");
        builder.HasIndex(e => e.RoleKey).HasDatabaseName("ix_calendar_saved_views_role_key");
    }
}

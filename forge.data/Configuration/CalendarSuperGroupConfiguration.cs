using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Calendar;

namespace Forge.Data.Configuration;

public class CalendarSuperGroupConfiguration : IEntityTypeConfiguration<CalendarSuperGroup>
{
    public void Configure(EntityTypeBuilder<CalendarSuperGroup> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(80);
        builder.Property(e => e.Name).HasMaxLength(120);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Color).HasMaxLength(20);
        builder.Property(e => e.IconName).HasMaxLength(60);
        builder.Property(e => e.IndustryGate).HasMaxLength(40);

        builder.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_calendar_super_groups_key");
    }
}

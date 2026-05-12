using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.NetHours).HasPrecision(8, 2);

        // Shifts effort — calendar-scoped fields. WorkingCalendarId
        // null = legacy work-center template; set = calendar-bound.
        builder.Property(e => e.PremiumMultiplier).HasPrecision(5, 2);
        builder.Property(e => e.CapacityHours).HasPrecision(8, 2);
        builder.HasOne(e => e.WorkingCalendar)
            .WithMany(c => c.Shifts)
            .HasForeignKey(e => e.WorkingCalendarId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.WorkingCalendarId);
    }
}

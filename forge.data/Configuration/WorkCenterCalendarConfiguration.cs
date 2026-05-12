using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WorkCenterCalendarConfiguration : IEntityTypeConfiguration<WorkCenterCalendar>
{
    public void Configure(EntityTypeBuilder<WorkCenterCalendar> builder)
    {
        builder.Property(e => e.AvailableHours).HasPrecision(8, 2);
        builder.Property(e => e.Reason).HasMaxLength(200);

        builder.HasIndex(e => new { e.WorkCenterId, e.Date }).IsUnique();

        builder.HasOne(e => e.WorkCenter)
            .WithMany(w => w.CalendarOverrides)
            .HasForeignKey(e => e.WorkCenterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

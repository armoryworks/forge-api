using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        // Hot path: "is this date a holiday on this calendar?" — keep both
        // the literal Date and the ObservedDate cheap to query.
        builder.HasIndex(e => new { e.WorkingCalendarId, e.Date });
        builder.HasIndex(e => new { e.WorkingCalendarId, e.ObservedDate });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class WorkingCalendarConfiguration : IEntityTypeConfiguration<WorkingCalendar>
{
    public void Configure(EntityTypeBuilder<WorkingCalendar> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TimeZone).HasMaxLength(64).IsRequired();

        // Exactly one default per install — same pattern as CompanyLocation.IsDefault.
        builder.HasIndex(e => e.IsDefault)
            .HasFilter("is_default = true")
            .IsUnique();

        builder.HasIndex(e => e.Name).IsUnique();

        builder.HasMany(e => e.Holidays)
            .WithOne(h => h.WorkingCalendar)
            .HasForeignKey(h => h.WorkingCalendarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EventAttendeeConfiguration : IEntityTypeConfiguration<EventAttendee>
{
    public void Configure(EntityTypeBuilder<EventAttendee> builder)
    {
        builder.HasOne(e => e.Event)
            .WithMany(e => e.Attendees)
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EventId, e.UserId }).IsUnique();
        builder.HasIndex(e => e.UserId);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
    }
}

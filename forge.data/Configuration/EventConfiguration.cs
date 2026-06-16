using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.IsAllDay).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.IsSystemGenerated).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.CreatedByUserId);
        builder.HasIndex(e => e.StartTime);
    }
}

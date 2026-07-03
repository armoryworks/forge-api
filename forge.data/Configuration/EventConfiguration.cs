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

        // compliance-calendar A-1: configurable taxonomy FK (nullable during expand).
        builder.HasOne(e => e.CalendarEventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeId)
            .HasConstraintName("fk_events__calendar_event_types_event_type_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.EventTypeId).HasDatabaseName("ix_events_event_type_id");

        // compliance-calendar A-4: tiered workflow.
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.WaivedReason).HasMaxLength(1000);
        builder.Property(e => e.EvidenceUrl).HasMaxLength(1000);
        builder.Property(e => e.IsBlocking).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.HasIndex(e => e.OwnerUserId).HasDatabaseName("ix_events_owner_user_id");
        builder.HasIndex(e => e.EvidenceDocumentSetId).HasDatabaseName("ix_events_evidence_document_set_id");
        builder.HasOne<DocumentSet>()
            .WithMany()
            .HasForeignKey(e => e.EvidenceDocumentSetId)
            .HasConstraintName("fk_events__document_sets_evidence_document_set_id")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

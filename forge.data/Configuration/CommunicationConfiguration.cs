using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CommunicationConfiguration : IEntityTypeConfiguration<Communication>
{
    public void Configure(EntityTypeBuilder<Communication> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        // Cascade kept from ContactInteraction, but the FK is optional now —
        // a communication can belong to a customer or vendor with no named
        // contact resolved.
        builder.HasOne(e => e.Contact)
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Subject).HasMaxLength(200).IsRequired();
        // Widened from varchar(4000): real message bodies exceed it and a
        // truncated body is a broken audit trail.
        builder.Property(e => e.Body).HasColumnType("text");
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Flow).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PartyType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.MatchConfidence).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ThreadId).HasMaxLength(998);
        builder.Property(e => e.ExternalId).HasMaxLength(200);
        builder.Property(e => e.FromAddress).HasMaxLength(320);

        builder.HasIndex(e => e.ContactId);
        builder.HasIndex(e => e.HandledByUserId);

        // The party timeline — "everything we've exchanged with Acme, newest first".
        builder.HasIndex(e => new { e.PartyType, e.PartyId, e.OccurredAt });

        // Threading. Partial because most rows outside email carry no thread.
        builder.HasIndex(e => e.ThreadId).HasFilter("thread_id IS NOT NULL");

        // Idempotency: a re-polled mailbox or re-delivered webhook must no-op.
        // Scoped by channel because provider id spaces do not overlap.
        builder.HasIndex(e => new { e.Channel, e.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        // The triage queue is "matched to nobody", so it is a partial index
        // rather than a separate table that would need keeping in sync.
        builder.HasIndex(e => e.OccurredAt)
            .HasFilter("party_id IS NULL AND deleted_at IS NULL");
    }
}

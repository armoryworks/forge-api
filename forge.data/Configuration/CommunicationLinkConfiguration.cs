using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CommunicationLinkConfiguration : IEntityTypeConfiguration<CommunicationLink>
{
    public void Configure(EntityTypeBuilder<CommunicationLink> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        // The rule that makes "never attach to a bare Part" structural rather
        // than merely intended. Part history is only meaningful at the
        // customer x part intersection; a Part link with no party is a claim
        // about every customer at once.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_communication_links_part_requires_party",
            "entity_type <> 'Part' OR party_id IS NOT NULL"));

        builder.Property(e => e.EntityType).HasMaxLength(30).IsRequired();
        builder.Property(e => e.PartyType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(e => e.Communication)
            .WithMany(c => c.Links)
            .HasForeignKey(e => e.CommunicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // "What was said about this quote / order / part."
        builder.HasIndex(e => new { e.EntityType, e.EntityId });

        // The customer × part intersection. Partial so it stays small — most
        // links are not parts — and it is the exact shape of the only query
        // that may read part history.
        builder.HasIndex(e => new { e.PartyId, e.EntityId })
            .HasFilter("entity_type = 'Part'")
            .HasDatabaseName("ix_communication_links_party_part");

        // Filing the same message against the same thing twice is a no-op, not
        // a second row.
        builder.HasIndex(e => new { e.CommunicationId, e.EntityType, e.EntityId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}

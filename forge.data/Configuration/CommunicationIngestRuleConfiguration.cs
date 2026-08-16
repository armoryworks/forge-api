using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class CommunicationIngestRuleConfiguration : IEntityTypeConfiguration<CommunicationIngestRule>
{
    public void Configure(EntityTypeBuilder<CommunicationIngestRule> builder)
    {
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.ConfidenceWhenMatched);

        builder.Property(e => e.MatchType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Pattern).HasMaxLength(320).IsRequired();
        builder.Property(e => e.PartyType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(1000);

        // One rule per pattern per type. Filtered so a deleted rule's pattern
        // can be re-added rather than being permanently burnt.
        builder.HasIndex(e => new { e.MatchType, e.Pattern })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // The ingestion hot path: given a sender, is anything enabled for it.
        builder.HasIndex(e => new { e.Pattern, e.IsEnabled });
    }
}

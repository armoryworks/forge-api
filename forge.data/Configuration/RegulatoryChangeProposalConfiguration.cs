using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities.Regulatory;

namespace Forge.Data.Configuration;

public class RegulatoryChangeProposalConfiguration : IEntityTypeConfiguration<RegulatoryChangeProposal>
{
    public void Configure(EntityTypeBuilder<RegulatoryChangeProposal> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.SummaryUrl).HasMaxLength(1000);
        builder.Property(e => e.Details).HasMaxLength(4000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.RegulatorySourceId).HasDatabaseName("ix_regulatory_change_proposals_regulatory_source_id");
        builder.HasIndex(e => e.Status).HasDatabaseName("ix_regulatory_change_proposals_status");
        builder.HasIndex(e => e.TargetEventTypeId).HasDatabaseName("ix_regulatory_change_proposals_target_event_type_id");

        builder.HasOne(e => e.RegulatorySource)
            .WithMany()
            .HasForeignKey(e => e.RegulatorySourceId)
            .HasConstraintName("fk_regulatory_change_proposals__regulatory_sources_regulatory_source_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TargetEventType)
            .WithMany()
            .HasForeignKey(e => e.TargetEventTypeId)
            .HasConstraintName("fk_regulatory_change_proposals__calendar_event_types_target_event_type_id")
            .OnDelete(DeleteBehavior.SetNull);
    }
}

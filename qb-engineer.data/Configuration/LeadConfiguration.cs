using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QBEngineer.Core.Entities;

namespace QBEngineer.Data.Configuration;

public class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.Ignore(e => e.IsDeleted);

        builder.Property(e => e.CompanyName).HasMaxLength(200);
        builder.Property(e => e.ContactName).HasMaxLength(200);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Source).HasMaxLength(100);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.LostReason).HasMaxLength(500);
        builder.Property(e => e.CustomFieldValues).HasColumnType("jsonb");

        // Lead → Customer 1:1 with the inverse nav on the customer side
        // (Customer.SourceLead). FK lives on Lead.ConvertedCustomerId.
        // SetNull on customer delete preserves the lead row's history of
        // having been converted, even if the customer is later soft-deleted.
        // Note: HasOne/WithOne enforces uniqueness on the FK at the EF model
        // level (one lead per converted customer). If a use case emerges
        // for multiple leads collapsing into one customer, revisit and
        // model that explicitly via a Lead-merge entity rather than
        // loosening this constraint.
        builder.HasOne(e => e.ConvertedCustomer)
            .WithOne(c => c.SourceLead)
            .HasForeignKey<Lead>(e => e.ConvertedCustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Phase 1r / Batch 5 — optional campaign FK. SetNull preserves
        // the lead row when a campaign gets soft-deleted; outreach-state
        // is a plain enum stored as string for forward-compat.
        builder.HasOne(e => e.Campaign)
            .WithMany()
            .HasForeignKey(e => e.CampaignId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.CampaignId);

        builder.Property(e => e.OutreachState).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(e => e.OutreachState);

        // Phase 1r / Batches 9-11 — intelligence + assignment columns.
        builder.HasOne(e => e.LeadSource)
            .WithMany()
            .HasForeignKey(e => e.LeadSourceId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.LeadSourceId);
        builder.HasIndex(e => e.AssignedToUserId);
        builder.HasIndex(e => e.IcpScore);

        // Phase 1r / Batch 12 — optional Account FK. SetNull preserves
        // the lead when its Account gets soft-deleted; orphan leads
        // remain queryable via the legacy flat-lead shape.
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.AccountId);

        // Phase 1r / Batches 13-14 — mfg compliance state machines.
        builder.Property(e => e.CapabilityFit).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.NdaState).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.ExportControl).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(e => e.CapabilityFit);
    }
}

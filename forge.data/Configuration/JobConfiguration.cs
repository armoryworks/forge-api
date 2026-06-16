using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.EstimatedBurdenCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.EstimatedLaborCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.EstimatedMaterialCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.EstimatedSubcontractCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.QuotedPrice).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Ignore(e => e.IsDeleted);

        // WU-11 / TODO E1 — optimistic locking
        builder.Property(e => e.Version).HasDefaultValue(1u);

        builder.HasIndex(e => e.JobNumber).IsUnique();
        builder.HasIndex(e => new { e.TrackTypeId, e.CurrentStageId });
        builder.HasIndex(e => e.AssigneeId);
        builder.HasIndex(e => e.DueDate);

        builder.Property(e => e.JobNumber).HasMaxLength(20);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.DispositionNotes).HasMaxLength(2000);
        builder.Property(e => e.CustomFieldValues).HasColumnType("jsonb");

        // Job Costing
        builder.Ignore(e => e.EstimatedTotalCost);
        builder.Ignore(e => e.EstimatedMarginPercent);
        builder.Property(e => e.EstimatedMaterialCost).HasPrecision(18, 4);
        builder.Property(e => e.EstimatedLaborCost).HasPrecision(18, 4);
        builder.Property(e => e.EstimatedBurdenCost).HasPrecision(18, 4);
        builder.Property(e => e.EstimatedSubcontractCost).HasPrecision(18, 4);
        builder.Property(e => e.QuotedPrice).HasPrecision(18, 4);

        builder.HasOne(e => e.TrackType)
            .WithMany(t => t.Jobs)
            .HasForeignKey(e => e.TrackTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CurrentStage)
            .WithMany(s => s.Jobs)
            .HasForeignKey(e => e.CurrentStageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Customer)
            .WithMany(c => c.Jobs)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.PartId);
        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ParentJobId);
        builder.HasMany(j => j.ChildJobs)
            .WithOne(j => j.ParentJob)
            .HasForeignKey(j => j.ParentJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CoverPhotoFileId);
        builder.HasOne(e => e.CoverPhotoFile)
            .WithMany()
            .HasForeignKey(e => e.CoverPhotoFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.MrpPlannedOrderId);
        builder.HasOne(e => e.MrpPlannedOrder)
            .WithMany()
            .HasForeignKey(e => e.MrpPlannedOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Phase 3 H4 / WU-20 — Job pins the BOM revision in effect when
        // released. Restrict deletion of a BomRevision while jobs reference
        // it (compliance reconstruction depends on the snapshot living on).
        builder.HasIndex(e => e.BomRevisionIdAtRelease);
        builder.HasOne(e => e.BomRevisionAtRelease)
            .WithMany()
            .HasForeignKey(e => e.BomRevisionIdAtRelease)
            .OnDelete(DeleteBehavior.Restrict);

        // Pro Services rollout — engagement axis fields. FK columns only;
        // navigation properties intentionally omitted on Job to keep the
        // read surface light. Loaders that need to expand these resolve
        // ReferenceData / Quote independently.
        builder.Property(e => e.RetainerHours).HasPrecision(10, 2);
        builder.Property(e => e.RetainerBalanceHours).HasPrecision(10, 2);

        builder.HasIndex(e => e.EngagementTypeId);
        builder.HasIndex(e => e.ProjectPhaseId);
        builder.HasIndex(e => e.SowId);
    }
}

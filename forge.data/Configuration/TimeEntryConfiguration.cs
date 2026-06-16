using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.BurdenCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.LaborCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.Property(e => e.EntryType).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.ActualLaborCost).HasDefaultValueSql("0.0").ValueGeneratedNever();
        builder.HasOne(t => t.Job)
            .WithMany()
            .HasForeignKey(t => t.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.UserId, t.Date });
        builder.HasIndex(t => t.JobId);
        builder.HasIndex(t => t.OperationId);
        builder.HasIndex(t => t.WorkCenterId);

        builder.Property(t => t.LaborCost).HasPrecision(18, 4);
        builder.Property(t => t.BurdenCost).HasPrecision(18, 4);

        builder.HasOne(t => t.Operation)
            .WithMany()
            .HasForeignKey(t => t.OperationId)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull (not Restrict): work centers can be retired; existing
        // time entries should keep their dollars/hours but lose the
        // center pointer. The historical OperationId still anchors the
        // entry to the routing it was logged against.
        builder.HasOne(t => t.WorkCenter)
            .WithMany()
            .HasForeignKey(t => t.WorkCenterId)
            .OnDelete(DeleteBehavior.SetNull);

        // Pro Services rollout — billable / non-billable split. FK columns
        // only; nav properties omitted to keep the read surface light.
        // IsBillable defaults to true at the DB level so the backfill for
        // existing rows preserves manufacturing semantics (all existing
        // entries are treated as billable for cost-rollup purposes).
        builder.Property(t => t.IsBillable).HasDefaultValue(true);
        builder.Property(t => t.BillRate).HasPrecision(10, 2);
        builder.Property(t => t.BillRateCurrency).HasMaxLength(3);
        builder.HasIndex(t => t.ActivityTypeId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Forge.Core.Entities;

namespace Forge.Data.Configuration;

public class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {

        // Legacy backfill defaults — declared to match the deployed schema so the squashed
        // InitialBaseline is a schema no-op (squash plan §3.3). Vestigial; revisit separately.
        builder.Property(e => e.InspectionFrequency).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.RequiresReceivingInspection).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.ExcludeFromAutoPo).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Property(e => e.SafetyStockQty).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.Name).HasDefaultValueSql("''").ValueGeneratedNever();
        builder.Property(e => e.InventoryClass).HasDefaultValueSql("'Component'").ValueGeneratedNever();
        builder.Property(e => e.ProcurementSource).HasDefaultValueSql("'Buy'").ValueGeneratedNever();
        builder.Property(e => e.TraceabilityType).HasDefaultValueSql("'None'").ValueGeneratedNever();
        builder.Property(e => e.IsLicense).HasDefaultValueSql("false").ValueGeneratedNever();
        builder.Ignore(e => e.IsDeleted);
        // Phase 3 H2 / WU-12: IActiveAware contract member — derived from Status.
        builder.Ignore(e => e.IsActiveForNewTransactions);

        builder.HasIndex(e => e.PartNumber).IsUnique();

        builder.Property(e => e.PartNumber).HasMaxLength(50);
        // Name is the canonical short identifier (required). Indexed because
        // parts are routinely searched/sorted by name in lists, BOM rows, and
        // entity pickers.
        builder.Property(e => e.Name).IsRequired().HasMaxLength(256);
        builder.HasIndex(e => e.Name);
        builder.Property(e => e.Description).HasMaxLength(2000).IsRequired(false);
        builder.Property(e => e.Revision).HasMaxLength(10);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.ExternalRef).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.CustomFieldValues).HasColumnType("jsonb");

        // Pillar 1 — Type decomposition: three orthogonal axes (procurement
        // source, inventory class, item kind) are the canonical answer to
        // "what kind of part is this?". The legacy single-axis PartType enum
        // was retired pre-beta in favour of these three axes.
        builder.Property(e => e.ProcurementSource).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.InventoryClass).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(e => new { e.ProcurementSource, e.InventoryClass });

        builder.HasIndex(e => e.ItemKindId);
        builder.HasOne(e => e.ItemKind)
            .WithMany()
            .HasForeignKey(e => e.ItemKindId)
            .OnDelete(DeleteBehavior.SetNull);

        // Tier 0 additions
        builder.Property(e => e.TraceabilityType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.AbcClass).HasConversion<string>().HasMaxLength(2);

        // Pillar 2 — Tier 2: Measurement profile + valuation.
        // See phase-4-output/part-type-field-relevance.md § 8 (Tier 2).
        builder.HasIndex(e => e.MaterialSpecId);
        builder.HasOne(e => e.MaterialSpec)
            .WithMany()
            .HasForeignKey(e => e.MaterialSpecId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.WeightEach).HasPrecision(18, 4);
        builder.Property(e => e.WeightDisplayUnit).HasMaxLength(8);
        builder.Property(e => e.LengthMm).HasPrecision(18, 4);
        builder.Property(e => e.WidthMm).HasPrecision(18, 4);
        builder.Property(e => e.HeightMm).HasPrecision(18, 4);
        builder.Property(e => e.DimensionDisplayUnit).HasMaxLength(8);
        builder.Property(e => e.VolumeMl).HasPrecision(18, 4);
        builder.Property(e => e.VolumeDisplayUnit).HasMaxLength(8);

        builder.HasIndex(e => e.ValuationClassId);
        builder.HasOne(e => e.ValuationClass)
            .WithMany()
            .HasForeignKey(e => e.ValuationClassId)
            .OnDelete(DeleteBehavior.SetNull);

        // Pillar 2 — Tier 3: Compliance + classification.
        // See phase-4-output/part-type-field-relevance.md § 8 (Tier 3).
        builder.Property(e => e.HtsCode).HasMaxLength(20);
        builder.Property(e => e.HazmatClass).HasMaxLength(20);
        builder.Property(e => e.BackflushPolicy).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.IsKit).HasDefaultValue(false);
        builder.Property(e => e.IsConfigurable).HasDefaultValue(false);

        builder.HasIndex(e => e.DefaultBinId);
        builder.HasOne(e => e.DefaultBin)
            .WithMany()
            .HasForeignKey(e => e.DefaultBinId)
            .OnDelete(DeleteBehavior.SetNull);

        // SourcePartId: self-FK without a navigation property to avoid EF
        // cycle issues. SetNull so deleting the source doesn't cascade.
        builder.HasIndex(e => e.SourcePartId);
        builder.HasOne<Part>()
            .WithMany()
            .HasForeignKey(e => e.SourcePartId)
            .OnDelete(DeleteBehavior.SetNull);

        // MRP planning
        builder.Property(e => e.FixedOrderQuantity).HasPrecision(18, 4);
        builder.Property(e => e.MinimumOrderQuantity).HasPrecision(18, 4);
        builder.Property(e => e.OrderMultiple).HasPrecision(18, 4);
        builder.Property(e => e.IsMrpPlanned).HasDefaultValue(false);

        builder.HasIndex(e => e.PreferredVendorId);
        builder.HasOne(e => e.PreferredVendor)
            .WithMany()
            .HasForeignKey(e => e.PreferredVendorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.ToolingAssetId);
        builder.HasOne(e => e.ToolingAsset)
            .WithMany()
            .HasForeignKey(e => e.ToolingAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        // Phase 3 H4 / WU-20 — pointer to active BomRevision. SetNull on
        // the FK so deleting a revision (rare; cascades from the part) does
        // not cascade-orphan-loop. Matched-side relationship is configured
        // on BomRevision.Part (WithMany BomRevisions).
        builder.HasIndex(e => e.CurrentBomRevisionId);
        builder.HasOne(e => e.CurrentBomRevision)
            .WithMany()
            .HasForeignKey(e => e.CurrentBomRevisionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Workflow Pattern Phase 2 / D3 — manual cost override + pointer at
        // active CostCalculation snapshot. SetNull because deleting a calc
        // row should leave the part intact.
        builder.Property(e => e.ManualCostOverride).HasColumnType("decimal(18,4)");
        builder.HasIndex(e => e.CurrentCostCalculationId);
        builder.HasOne(e => e.CurrentCostCalculation)
            .WithMany()
            .HasForeignKey(e => e.CurrentCostCalculationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

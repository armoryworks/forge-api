using Riok.Mapperly.Abstractions;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Mappers;

[Mapper]
public static partial class PartMapper
{
    /// <summary>
    /// Maps a Part entity to a PartDetailResponseModel.
    /// BOM lines and usage lists must be provided separately as they require sub-mapping.
    /// Pricing fields are populated from a <see cref="ResolvedPartPrice"/> when supplied
    /// (callers should resolve via <c>IPartPricingResolver</c>); when null the response
    /// falls back to the Default rung values.
    /// </summary>
    public static PartDetailResponseModel ToDetailModel(
        this Part part,
        List<BOMLineResponseModel>? bomLines = null,
        List<BOMUsageResponseModel>? usedIn = null,
        ResolvedPartPrice? resolvedPrice = null)
    {
        return new PartDetailResponseModel(
            Id: part.Id,
            PartNumber: part.PartNumber,
            Name: part.Name,
            Description: part.Description,
            Revision: part.Revision,
            Status: part.Status,
            // Pillar 1 — Decomposed type axes
            ProcurementSource: part.ProcurementSource,
            InventoryClass: part.InventoryClass,
            ItemKindId: part.ItemKindId,
            ItemKindLabel: part.ItemKind?.Label,
            // Tier 0 additions
            TraceabilityType: part.TraceabilityType,
            AbcClass: part.AbcClass,
            // Pillar 2 — Tier 2 material spec FK
            MaterialSpecId: part.MaterialSpecId,
            MaterialSpecLabel: part.MaterialSpec?.Label,
            ExternalId: part.ExternalId,
            ExternalRef: part.ExternalRef,
            Provider: part.Provider,
            PreferredVendorId: part.PreferredVendorId,
            PreferredVendorName: part.PreferredVendor?.CompanyName,
            MinStockThreshold: part.MinStockThreshold,
            ReorderPoint: part.ReorderPoint,
            ReorderQuantity: part.ReorderQuantity,
            SafetyStockDays: part.SafetyStockDays,
            ToolingAssetId: part.ToolingAssetId,
            ToolingAssetName: part.ToolingAsset?.Name,
            // Workflow Pattern Phase 5 — surfaces cost gates for the hasCost predicate.
            ManualCostOverride: part.ManualCostOverride,
            CurrentCostCalculationId: part.CurrentCostCalculationId,
            // Pillar 2 — Tier 2 measurement profile
            WeightEach: part.WeightEach,
            WeightDisplayUnit: part.WeightDisplayUnit,
            LengthMm: part.LengthMm,
            WidthMm: part.WidthMm,
            HeightMm: part.HeightMm,
            DimensionDisplayUnit: part.DimensionDisplayUnit,
            VolumeMl: part.VolumeMl,
            VolumeDisplayUnit: part.VolumeDisplayUnit,
            // Pillar 2 — Tier 2 valuation
            ValuationClassId: part.ValuationClassId,
            ValuationClassLabel: part.ValuationClass?.Label,
            // Pillar 2 — Tier 3 compliance + classification
            HtsCode: part.HtsCode,
            HazmatClass: part.HazmatClass,
            ShelfLifeDays: part.ShelfLifeDays,
            BackflushPolicy: part.BackflushPolicy,
            IsKit: part.IsKit,
            IsConfigurable: part.IsConfigurable,
            DefaultBinId: part.DefaultBinId,
            SourcePartId: part.SourcePartId,
            BomLines: bomLines ?? [],
            UsedIn: usedIn ?? [],
            CreatedAt: part.CreatedAt,
            UpdatedAt: part.UpdatedAt,
            // Pillar 4 Phase 2 — UoM cluster (id + resolved code/label)
            StockUomId: part.StockUomId,
            StockUomCode: part.StockUom?.Code,
            StockUomLabel: part.StockUom?.Name,
            PurchaseUomId: part.PurchaseUomId,
            PurchaseUomCode: part.PurchaseUom?.Code,
            PurchaseUomLabel: part.PurchaseUom?.Name,
            SalesUomId: part.SalesUomId,
            SalesUomCode: part.SalesUom?.Code,
            SalesUomLabel: part.SalesUom?.Name,
            // Pillar 4 Phase 2 — MRP cluster
            IsMrpPlanned: part.IsMrpPlanned,
            LotSizingRule: part.LotSizingRule,
            FixedOrderQuantity: part.FixedOrderQuantity,
            MinimumOrderQuantity: part.MinimumOrderQuantity,
            OrderMultiple: part.OrderMultiple,
            PlanningFenceDays: part.PlanningFenceDays,
            DemandFenceDays: part.DemandFenceDays,
            // Pillar 4 Phase 2 — Quality cluster (receiving inspection)
            RequiresReceivingInspection: part.RequiresReceivingInspection,
            ReceivingInspectionTemplateId: part.ReceivingInspectionTemplateId,
            InspectionFrequency: part.InspectionFrequency,
            InspectionSkipAfterN: part.InspectionSkipAfterN,
            EffectivePrice: resolvedPrice?.UnitPrice ?? 0m,
            EffectivePriceCurrency: resolvedPrice?.Currency ?? "USD",
            EffectivePriceSource: (resolvedPrice?.Source ?? PartPriceSource.Default).ToString());
    }
}

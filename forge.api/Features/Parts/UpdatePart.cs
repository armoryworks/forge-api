using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Forge.Api.Features.VendorParts;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public record UpdatePartCommand(int Id, UpdatePartRequestModel Data) : IRequest<PartDetailResponseModel>;

public class UpdatePartCommandValidator : AbstractValidator<UpdatePartCommand>
{
    public UpdatePartCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name)
            .NotEmpty()
            .MaximumLength(256)
            .When(x => x.Data.Name is not null);
        RuleFor(x => x.Data.Description).MaximumLength(2000).When(x => x.Data.Description is not null);
        RuleFor(x => x.Data.Revision).MaximumLength(10).When(x => x.Data.Revision is not null);
        RuleFor(x => x.Data.PartNumber).NotEmpty().MaximumLength(50).When(x => x.Data.PartNumber is not null);
        // Pillar 4 Phase 2 — mirror the entity-config max lengths for the new
        // editable string fields (see PartConfiguration.cs).
        RuleFor(x => x.Data.HtsCode).MaximumLength(20).When(x => x.Data.HtsCode is not null);
        RuleFor(x => x.Data.HazmatClass).MaximumLength(20).When(x => x.Data.HazmatClass is not null);
        RuleFor(x => x.Data.WeightDisplayUnit).MaximumLength(8).When(x => x.Data.WeightDisplayUnit is not null);
        RuleFor(x => x.Data.DimensionDisplayUnit).MaximumLength(8).When(x => x.Data.DimensionDisplayUnit is not null);
        RuleFor(x => x.Data.VolumeDisplayUnit).MaximumLength(8).When(x => x.Data.VolumeDisplayUnit is not null);
        RuleFor(x => x.Data.ExpectedYieldPercent)
            .Must(y => y < 0m || (y > 0m && y <= 100m && decimal.Round(y.Value, 2) == y))
            .WithMessage("Expected yield must be more than 0 and at most 100 percent, with up to 2 decimal places.")
            .When(x => x.Data.ExpectedYieldPercent.HasValue);
        RuleFor(x => x.Data.ManualCostOverride).UnitScale();
    }
}

public class UpdatePartHandler(
    IPartRepository repo,
    ISystemSettingRepository systemSettings,
    IBarcodeService barcodeService,
    IBusinessIdentifierService identifiers,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    AppDbContext db,
    ILogger<UpdatePartHandler> logger) : IRequestHandler<UpdatePartCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(UpdatePartCommand request, CancellationToken cancellationToken)
    {
        var part = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.Id} not found");

        var data = request.Data;

        // User-settable part number — only when manual numbers are enabled, and only after a
        // uniqueness check that excludes this part. The DB unique index is the final backstop.
        var partNumberChanged = false;
        if (data.PartNumber is not null)
        {
            var newNumber = data.PartNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, part.PartNumber, StringComparison.Ordinal))
            {
                if (!await PartNumberCheck.ManualNumbersAllowedAsync(systemSettings, cancellationToken))
                    throw PartNumberCheck.ManualNumbersOffOnRename();
                await PartNumberCheck.EnsureAvailableAsync(repo, newNumber, part.Id, cancellationToken);
                // Record the rename in the identifier registry: ensure the current number is on record
                // (covers pre-registry parts), then supersede it — the old number stays resolvable.
                await identifiers.IssueAsync(BusinessEntityType.Part, part.Id, part.PartNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Part, part.Id, newNumber, cancellationToken);
                part.PartNumber = newNumber;
                partNumberChanged = true;
            }
        }

        if (data.Name is not null) part.Name = data.Name.Trim();
        if (data.Description is not null)
        {
            // Empty / whitespace clears the optional Description field.
            var trimmed = data.Description.Trim();
            part.Description = trimmed.Length == 0 ? null : trimmed;
        }
        if (data.Revision is not null)
        {
            var previousRevision = part.Revision;
            part.Revision = data.Revision.Trim();
            await PartRevisionBaseline.RenameCurrentAsync(db, part.Id, previousRevision, part.Revision, cancellationToken);
        }
        if (data.Status.HasValue) part.Status = data.Status.Value;
        if (data.ProcurementSource.HasValue) part.ProcurementSource = data.ProcurementSource.Value;
        if (data.InventoryClass.HasValue) part.InventoryClass = data.InventoryClass.Value;
        // Tier 0 — traceability + ABC class. (OEM identity moved to VendorPart.)
        if (data.TraceabilityType.HasValue) part.TraceabilityType = data.TraceabilityType.Value;
        if (data.AbcClass.HasValue) part.AbcClass = data.AbcClass.Value;
        if (data.ToolingAssetId.HasValue) part.ToolingAssetId = data.ToolingAssetId.Value == 0 ? null : data.ToolingAssetId.Value;
        if (data.PreferredVendorId.HasValue)
            await PreferredVendorSync.SetPartPreferredVendorAsync(
                db, part, data.PreferredVendorId.Value == 0 ? null : data.PreferredVendorId.Value, cancellationToken);
        if (data.MinStockThreshold.HasValue) part.MinStockThreshold = data.MinStockThreshold.Value == 0 ? null : data.MinStockThreshold.Value;
        if (data.ReorderPoint.HasValue) part.ReorderPoint = data.ReorderPoint.Value == 0 ? null : data.ReorderPoint.Value;
        if (data.ReorderQuantity.HasValue) part.ReorderQuantity = data.ReorderQuantity.Value == 0 ? null : data.ReorderQuantity.Value;
        if (data.SafetyStockDays.HasValue) part.SafetyStockDays = data.SafetyStockDays.Value;
        // Workflow Pattern Phase 5 — manual cost override (Tier 1). -1 sentinel clears.
        if (data.ManualCostOverride.HasValue)
        {
            part.ManualCostOverride = data.ManualCostOverride.Value < 0 ? null : data.ManualCostOverride.Value;
        }

        // Pillar 4 Phase 2 — UoM cluster. -1 sentinel clears the FK to null;
        // any other value sets it. Null = no change.
        if (data.StockUomId.HasValue)
            part.StockUomId = data.StockUomId.Value < 0 ? null : data.StockUomId.Value;
        if (data.PurchaseUomId.HasValue)
            part.PurchaseUomId = data.PurchaseUomId.Value < 0 ? null : data.PurchaseUomId.Value;
        if (data.SalesUomId.HasValue)
            part.SalesUomId = data.SalesUomId.Value < 0 ? null : data.SalesUomId.Value;

        // Pillar 4 Phase 2 — MRP cluster. Enums and bool? are set-only (no clear);
        // decimals/ints use the < 0 sentinel to clear.
        if (data.IsMrpPlanned.HasValue) part.IsMrpPlanned = data.IsMrpPlanned.Value;
        if (data.LotSizingRule.HasValue) part.LotSizingRule = data.LotSizingRule.Value;
        if (data.FixedOrderQuantity.HasValue)
            part.FixedOrderQuantity = data.FixedOrderQuantity.Value < 0 ? null : data.FixedOrderQuantity.Value;
        if (data.MinimumOrderQuantity.HasValue)
            part.MinimumOrderQuantity = data.MinimumOrderQuantity.Value < 0 ? null : data.MinimumOrderQuantity.Value;
        if (data.OrderMultiple.HasValue)
            part.OrderMultiple = data.OrderMultiple.Value < 0 ? null : data.OrderMultiple.Value;
        if (data.ExpectedYieldPercent.HasValue)
            part.ExpectedYieldPercent = data.ExpectedYieldPercent.Value < 0 ? null : data.ExpectedYieldPercent.Value;
        if (data.PlanningFenceDays.HasValue)
            part.PlanningFenceDays = data.PlanningFenceDays.Value < 0 ? null : data.PlanningFenceDays.Value;
        if (data.DemandFenceDays.HasValue)
            part.DemandFenceDays = data.DemandFenceDays.Value < 0 ? null : data.DemandFenceDays.Value;

        // Pillar 4 Phase 2 — Quality cluster (receiving inspection).
        if (data.RequiresReceivingInspection.HasValue) part.RequiresReceivingInspection = data.RequiresReceivingInspection.Value;
        if (data.ReceivingInspectionTemplateId.HasValue)
            part.ReceivingInspectionTemplateId = data.ReceivingInspectionTemplateId.Value < 0
                ? null
                : data.ReceivingInspectionTemplateId.Value;
        if (data.InspectionFrequency.HasValue) part.InspectionFrequency = data.InspectionFrequency.Value;
        if (data.InspectionSkipAfterN.HasValue)
            part.InspectionSkipAfterN = data.InspectionSkipAfterN.Value < 0
                ? null
                : data.InspectionSkipAfterN.Value;

        // Pillar 4 Phase 2 — Material cluster (measurement profile + valuation).
        if (data.MaterialSpecId.HasValue)
            part.MaterialSpecId = data.MaterialSpecId.Value < 0 ? null : data.MaterialSpecId.Value;
        if (data.WeightEach.HasValue)
            part.WeightEach = data.WeightEach.Value < 0 ? null : data.WeightEach.Value;
        if (data.WeightDisplayUnit is not null)
        {
            var trimmed = data.WeightDisplayUnit.Trim();
            part.WeightDisplayUnit = trimmed.Length == 0 ? null : trimmed;
        }
        if (data.LengthMm.HasValue)
            part.LengthMm = data.LengthMm.Value < 0 ? null : data.LengthMm.Value;
        if (data.WidthMm.HasValue)
            part.WidthMm = data.WidthMm.Value < 0 ? null : data.WidthMm.Value;
        if (data.HeightMm.HasValue)
            part.HeightMm = data.HeightMm.Value < 0 ? null : data.HeightMm.Value;
        if (data.DimensionDisplayUnit is not null)
        {
            var trimmed = data.DimensionDisplayUnit.Trim();
            part.DimensionDisplayUnit = trimmed.Length == 0 ? null : trimmed;
        }
        if (data.VolumeMl.HasValue)
            part.VolumeMl = data.VolumeMl.Value < 0 ? null : data.VolumeMl.Value;
        if (data.VolumeDisplayUnit is not null)
        {
            var trimmed = data.VolumeDisplayUnit.Trim();
            part.VolumeDisplayUnit = trimmed.Length == 0 ? null : trimmed;
        }
        if (data.ValuationClassId.HasValue)
            part.ValuationClassId = data.ValuationClassId.Value < 0 ? null : data.ValuationClassId.Value;

        // Pillar 4 Phase 2 — Tier 3 compliance / classification + ad-hoc fields.
        if (data.HazmatClass is not null)
        {
            var trimmed = data.HazmatClass.Trim();
            part.HazmatClass = trimmed.Length == 0 ? null : trimmed;
        }
        if (data.ShelfLifeDays.HasValue)
            part.ShelfLifeDays = data.ShelfLifeDays.Value < 0 ? null : data.ShelfLifeDays.Value;
        if (data.BackflushPolicy.HasValue) part.BackflushPolicy = data.BackflushPolicy.Value;
        if (data.IsKit.HasValue) part.IsKit = data.IsKit.Value;
        if (data.IsConfigurable.HasValue) part.IsConfigurable = data.IsConfigurable.Value;
        if (data.DefaultBinId.HasValue)
            part.DefaultBinId = data.DefaultBinId.Value < 0 ? null : data.DefaultBinId.Value;
        if (data.SourcePartId.HasValue)
            part.SourcePartId = data.SourcePartId.Value < 0 ? null : data.SourcePartId.Value;
        if (data.HtsCode is not null)
        {
            var trimmed = data.HtsCode.Trim();
            part.HtsCode = trimmed.Length == 0 ? null : trimmed;
        }

        db.ChangeTracker.DetectChanges();
        if (ActivityChangeSummary.Updated(ActivityChangeSummary.ChangedProperties(db.Entry(part))) is string summary)
            db.LogActivityAt("updated", summary, ("Part", part.Id));

        await repo.SaveChangesAsync(cancellationToken);

        // The auto-generated barcode derives from the part number (PRT-<number>), so a
        // rename re-syncs the System barcode; manual alias barcodes are left untouched.
        if (partNumberChanged)
            await barcodeService.RefreshPartBarcodeAsync(part.Id, cancellationToken);

        // Enqueue QB Item update if part is linked and accounting is connected
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
            if (accountingService is not null)
            {
                var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
                if (syncStatus.Connected && part.ExternalId is not null)
                {
                    var item = new AccountingItem(
                        part.ExternalId, part.PartNumber, part.Name,
                        "NonInventory", null, null, part.PartNumber, part.Status == Core.Enums.PartStatus.Active);
                    var payload = JsonSerializer.Serialize(item);
                    await syncQueue.EnqueueAsync("Part", part.Id, "UpdateItem", payload, cancellationToken);
                    logger.LogInformation("Enqueued UpdateItem sync for Part {PartId}", part.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue item sync for Part {PartId} — continuing", part.Id);
        }

        return (await repo.GetDetailAsync(part.Id, cancellationToken))!;
    }
}

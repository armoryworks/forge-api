using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Duplicates a part as a new Draft part, carrying its descriptive and engineering fields and,
/// on request, its BOM, routing (operations plus their material links) and vendor sources.
/// Inventory, revisions, prices, accounting links, attachments, alternates, piece rates and SPC
/// characteristics are never copied. On a connected accounting install the new part is queued
/// for item creation, as a newly created part is.
/// </summary>
public record ClonePartCommand(int SourcePartId, ClonePartRequestModel Data) : IRequest<PartDetailResponseModel>;

public class ClonePartCommandValidator : AbstractValidator<ClonePartCommand>
{
    public ClonePartCommandValidator()
    {
        RuleFor(x => x.SourcePartId).GreaterThan(0);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Data.Description).MaximumLength(2000).When(x => x.Data.Description is not null);
        RuleFor(x => x.Data.PartNumber).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.Data.PartNumber));
    }
}

public class ClonePartHandler(
    AppDbContext db,
    IPartRepository repo,
    ISystemSettingRepository systemSettings,
    IBarcodeService barcodeService,
    IBusinessIdentifierService identifiers,
    IBomRevisionService bomRevisions,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    ISender sender,
    ILogger<ClonePartHandler> logger) : IRequestHandler<ClonePartCommand, PartDetailResponseModel>
{
    private const string AllowManualPartNumbersKey = "parts.allow_manual_numbers";

    public async Task<PartDetailResponseModel> Handle(ClonePartCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var source = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.SourcePartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.SourcePartId} not found");

        var partNumber = await ResolvePartNumberAsync(data.PartNumber, source.InventoryClass, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var part = CopyPart(source, partNumber, data);
        db.Parts.Add(part);

        var bomMap = data.CopyBom
            ? await CopyBomAsync(source.Id, part, cancellationToken)
            : new Dictionary<int, BOMLine>();
        var operationCount = data.CopyRouting
            ? await CopyRoutingAsync(source.Id, part, bomMap, cancellationToken)
            : 0;
        var vendorSourceCount = data.CopyVendorSources
            ? await CopyVendorSourcesAsync(source.Id, part, cancellationToken)
            : 0;

        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt(
            "created",
            $"Duplicated from {source.PartNumber}: {bomMap.Count} BOM lines, {operationCount} operations, {vendorSourceCount} vendor sources",
            ("Part", part.Id));
        await db.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.Part, part.Id, part.PartNumber, cancellationToken);
        await identifiers.IssueAsync(BusinessEntityType.Part, part.Id, part.PartNumber, cancellationToken);

        if (bomMap.Count > 0)
            await bomRevisions.CaptureCurrentStateAsync(
                part.Id, db.CurrentUserId, $"Duplicated from {source.PartNumber}", cancellationToken);

        await sender.Send(new RecalculatePartStandardCostCommand(part.Id), cancellationToken);

        await tx.CommitAsync(cancellationToken);

        await EnqueueAccountingItemAsync(part, cancellationToken);

        return (await repo.GetDetailAsync(part.Id, cancellationToken))!;
    }

    private static Part CopyPart(Part source, string partNumber, ClonePartRequestModel data) => new()
    {
        PartNumber = partNumber,
        Name = data.Name.Trim(),
        Description = data.Description is null
            ? source.Description
            : string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim(),
        Revision = "A",
        Status = PartStatus.Draft,
        ProcurementSource = source.ProcurementSource,
        InventoryClass = source.InventoryClass,
        ItemKindId = source.ItemKindId,
        TraceabilityType = source.TraceabilityType,
        AbcClass = source.AbcClass,
        MaterialSpecId = source.MaterialSpecId,
        WeightEach = source.WeightEach,
        WeightDisplayUnit = source.WeightDisplayUnit,
        LengthMm = source.LengthMm,
        WidthMm = source.WidthMm,
        HeightMm = source.HeightMm,
        DimensionDisplayUnit = source.DimensionDisplayUnit,
        VolumeMl = source.VolumeMl,
        VolumeDisplayUnit = source.VolumeDisplayUnit,
        ValuationClassId = source.ValuationClassId,
        HtsCode = source.HtsCode,
        HazmatClass = source.HazmatClass,
        ShelfLifeDays = source.ShelfLifeDays,
        BackflushPolicy = source.BackflushPolicy,
        IsKit = source.IsKit,
        IsConfigurable = source.IsConfigurable,
        SourcePartId = source.SourcePartId,
        LotSizingRule = source.LotSizingRule,
        FixedOrderQuantity = source.FixedOrderQuantity,
        MinimumOrderQuantity = source.MinimumOrderQuantity,
        OrderMultiple = source.OrderMultiple,
        PlanningFenceDays = source.PlanningFenceDays,
        DemandFenceDays = source.DemandFenceDays,
        IsMrpPlanned = source.IsMrpPlanned,
        RequiresReceivingInspection = source.RequiresReceivingInspection,
        ReceivingInspectionTemplateId = source.ReceivingInspectionTemplateId,
        InspectionFrequency = source.InspectionFrequency,
        InspectionSkipAfterN = source.InspectionSkipAfterN,
        CustomFieldValues = source.CustomFieldValues,
        StockUomId = source.StockUomId,
        PurchaseUomId = source.PurchaseUomId,
        SalesUomId = source.SalesUomId,
        ToolingAssetId = source.ToolingAssetId,
    };

    private async Task<Dictionary<int, BOMLine>> CopyBomAsync(int sourcePartId, Part part, CancellationToken ct)
    {
        var lines = await db.BOMLines.AsNoTracking()
            .Where(b => b.ParentPartId == sourcePartId)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .ToListAsync(ct);

        var map = new Dictionary<int, BOMLine>();
        foreach (var line in lines)
        {
            var copy = new BOMLine
            {
                ParentPart = part,
                ChildPartId = line.ChildPartId,
                Quantity = line.Quantity,
                ReferenceDesignator = line.ReferenceDesignator,
                SortOrder = line.SortOrder,
                SourceType = line.SourceType,
                LeadTimeDays = line.LeadTimeDays,
                Notes = line.Notes,
                UomId = line.UomId,
                VendorId = line.VendorId,
            };
            db.BOMLines.Add(copy);
            map[line.Id] = copy;
        }

        return map;
    }

    private async Task<int> CopyRoutingAsync(
        int sourcePartId, Part part, IReadOnlyDictionary<int, BOMLine> bomMap, CancellationToken ct)
    {
        var operations = await db.Operations.AsNoTracking()
            .Include(o => o.Materials)
            .Where(o => o.PartId == sourcePartId)
            .OrderBy(o => o.StepNumber).ThenBy(o => o.Id)
            .ToListAsync(ct);

        var map = new Dictionary<int, Operation>();
        foreach (var op in operations)
        {
            var copy = new Operation
            {
                Part = part,
                StepNumber = op.StepNumber,
                Title = op.Title,
                Instructions = op.Instructions,
                WorkCenterId = op.WorkCenterId,
                AssetId = op.AssetId,
                EstimatedMs = op.EstimatedMs,
                IsQcCheckpoint = op.IsQcCheckpoint,
                QcCriteria = op.QcCriteria,
                SetupMinutes = op.SetupMinutes,
                RunMinutesEach = op.RunMinutesEach,
                RunMinutesLot = op.RunMinutesLot,
                OverlapPercent = op.OverlapPercent,
                ScrapFactor = op.ScrapFactor,
                IsSubcontract = op.IsSubcontract,
                SubcontractVendorId = op.SubcontractVendorId,
                SubcontractCost = op.SubcontractCost,
                SubcontractLeadTimeDays = op.SubcontractLeadTimeDays,
                SubcontractInstructions = op.SubcontractInstructions,
                SubcontractTurnTimeDays = op.SubcontractTurnTimeDays,
                LaborRate = op.LaborRate,
                BurdenRate = op.BurdenRate,
                EstimatedLaborCost = op.EstimatedLaborCost,
                EstimatedBurdenCost = op.EstimatedBurdenCost,
            };

            foreach (var material in op.Materials)
            {
                if (!bomMap.TryGetValue(material.BomLineId, out var bomLine))
                    continue;
                copy.Materials.Add(new OperationMaterial
                {
                    Operation = copy,
                    BomLine = bomLine,
                    Quantity = material.Quantity,
                    Notes = material.Notes,
                });
            }

            db.Operations.Add(copy);
            map[op.Id] = copy;
        }

        foreach (var op in operations)
        {
            if (op.ReferencedOperationId is { } referencedId && map.TryGetValue(referencedId, out var referenced))
                map[op.Id].ReferencedOperation = referenced;
        }

        return operations.Count;
    }

    private async Task<int> CopyVendorSourcesAsync(int sourcePartId, Part part, CancellationToken ct)
    {
        var sources = await db.VendorParts.AsNoTracking()
            .Where(v => v.PartId == sourcePartId)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        foreach (var vp in sources)
        {
            db.VendorParts.Add(new VendorPart
            {
                Part = part,
                VendorId = vp.VendorId,
                VendorPartNumber = vp.VendorPartNumber,
                ManufacturerName = vp.ManufacturerName,
                VendorMpn = vp.VendorMpn,
                LeadTimeDays = vp.LeadTimeDays,
                MinOrderQty = vp.MinOrderQty,
                PackSize = vp.PackSize,
                CountryOfOrigin = vp.CountryOfOrigin,
                HtsCode = vp.HtsCode,
                IsManufacturer = vp.IsManufacturer,
                IsApproved = vp.IsApproved,
                IsPreferred = false,
                Certifications = vp.Certifications,
                Currency = vp.Currency,
                Incoterm = vp.Incoterm,
                Notes = vp.Notes,
            });
        }

        return sources.Count;
    }

    private async Task<string> ResolvePartNumberAsync(string? requested, InventoryClass inventoryClass, CancellationToken ct)
    {
        var supplied = requested?.Trim();
        if (string.IsNullOrWhiteSpace(supplied))
            return await repo.GetNextPartNumberAsync(inventoryClass, ct);

        if (!await ManualPartNumbersAllowedAsync(ct))
            throw new InvalidOperationException("Manual part numbers are not enabled; leave the part number blank to auto-number.");

        var taken = await db.Parts.IgnoreQueryFilters().AnyAsync(p => p.PartNumber == supplied, ct);
        if (taken)
            throw new InvalidOperationException($"Part number '{supplied}' is already in use.");

        return supplied;
    }

    private async Task EnqueueAccountingItemAsync(Part part, CancellationToken ct)
    {
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(ct);
            if (accountingService is null)
                return;

            var syncStatus = await accountingService.GetSyncStatusAsync(ct);
            if (!syncStatus.Connected)
                return;

            var item = new AccountingItem(
                null, part.PartNumber, part.Name,
                "NonInventory", null, null, part.PartNumber, true);
            await syncQueue.EnqueueAsync("Part", part.Id, "CreateItem", JsonSerializer.Serialize(item), ct);
            logger.LogInformation("Enqueued CreateItem sync for Part {PartId}", part.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue item sync for Part {PartId} — continuing", part.Id);
        }
    }

    private async Task<bool> ManualPartNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPartNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}

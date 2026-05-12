using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class PartRepository(AppDbContext db, IPartPricingResolver pricingResolver) : IPartRepository
{
    public async Task<List<PartListResponseModel>> GetPartsAsync(PartStatus? status, string? search, CancellationToken ct)
    {
        // Legacy non-paged path. Routes through GetPagedAsync with a wide page
        // so existing internal callers behave unchanged. New work should call
        // GetPagedAsync directly. (Phase 3 F7-partial / WU-17.)
        var paged = await GetPagedAsync(new PartListQuery
        {
            Status = status,
            Q = search,
            PageSize = 200,
            // Legacy callers expected partNumber-asc ordering — preserve that.
            Sort = "partNumber",
            Order = "asc",
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<PartListResponseModel>> GetPagedAsync(
        PartListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-partial / WU-17 — standardised paged-list contract.
        // Sort whitelisted; stable secondary sort by Id for deterministic
        // pagination across ties.
        var q = db.Parts.AsQueryable();

        // — Filters —
        if (query.Status.HasValue)
            q = q.Where(p => p.Status == query.Status.Value);
        else if (query.IsActive.HasValue)
        {
            // Phase 3 F7-partial: alias IsActive onto the Status enum so the
            // standardised filter dimension works on parts. true = anything
            // not Obsolete; false = Obsolete only.
            q = query.IsActive.Value
                ? q.Where(p => p.Status != PartStatus.Obsolete)
                : q.Where(p => p.Status == PartStatus.Obsolete);
        }

        if (query.ProcurementSource.HasValue)
            q = q.Where(p => p.ProcurementSource == query.ProcurementSource.Value);
        if (query.InventoryClass.HasValue)
            q = q.Where(p => p.InventoryClass == query.InventoryClass.Value);

        if (query.DefaultVendorId.HasValue)
            q = q.Where(p => p.PreferredVendorId == query.DefaultVendorId.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(p =>
                p.PartNumber.ToLower().Contains(term) ||
                p.Name.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        if (query.DateFrom.HasValue)
            q = q.Where(p => p.CreatedAt >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            q = q.Where(p => p.CreatedAt <= query.DateTo.Value);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Part> ordered = sortKey switch
        {
            "partnumber"         => desc ? q.OrderByDescending(p => p.PartNumber)         : q.OrderBy(p => p.PartNumber),
            "name"               => desc ? q.OrderByDescending(p => p.Name)               : q.OrderBy(p => p.Name),
            "description"        => desc ? q.OrderByDescending(p => p.Description)        : q.OrderBy(p => p.Description),
            "revision"           => desc ? q.OrderByDescending(p => p.Revision)           : q.OrderBy(p => p.Revision),
            "status"             => desc ? q.OrderByDescending(p => p.Status)             : q.OrderBy(p => p.Status),
            "procurementsource"  => desc ? q.OrderByDescending(p => p.ProcurementSource)  : q.OrderBy(p => p.ProcurementSource),
            "inventoryclass"     => desc ? q.OrderByDescending(p => p.InventoryClass)     : q.OrderBy(p => p.InventoryClass),
            "createdat"          => desc ? q.OrderByDescending(p => p.CreatedAt)          : q.OrderBy(p => p.CreatedAt),
            "updatedat"          => desc ? q.OrderByDescending(p => p.UpdatedAt)          : q.OrderBy(p => p.UpdatedAt),
            "id"                 => desc ? q.OrderByDescending(p => p.Id)                 : q.OrderBy(p => p.Id),
            _ => q.OrderByDescending(p => p.CreatedAt),
        };
        ordered = ordered.ThenBy(p => p.Id);

        // Project the page rows WITHOUT the price (Pillar 5 — pricing now flows
        // through IPartPricingResolver, not an inline projection). Pricing rows
        // are looked up after materialization via ResolveManyAsync.
        var rows = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(p => new
            {
                p.Id,
                p.PartNumber,
                p.Name,
                p.Description,
                p.Revision,
                p.Status,
                p.ProcurementSource,
                p.InventoryClass,
                BomEntryCount = p.BOMEntries.Count,
                p.CreatedAt,
            })
            .ToListAsync(ct);

        var partIds = rows.Select(r => r.Id).ToList();
        var prices = await pricingResolver.ResolveManyAsync(partIds, ct);

        // Pre-load any in-progress workflow runs for the page's parts so the
        // row-level "resume workflow" indicator can render without a per-row
        // round-trip. Single query, indexed by entity_id; build a dict keyed
        // on entity id so the per-row Select stays O(1).
        var pendingRuns = await db.WorkflowRuns
            .AsNoTracking()
            .Where(r => r.EntityType == "Part"
                && r.EntityId != null
                && partIds.Contains(r.EntityId.Value)
                && r.CompletedAt == null
                && r.AbandonedAt == null)
            .Select(r => new
            {
                r.Id,
                EntityId = r.EntityId!.Value,
                r.DefinitionId,
                r.CurrentStepId,
                r.Mode,
                r.LastActivityAt,
            })
            .ToListAsync(ct);
        var pendingByPartId = pendingRuns
            .GroupBy(r => r.EntityId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.LastActivityAt).First());

        var items = rows
            .Select(r =>
            {
                var price = prices[r.Id];
                PendingWorkflowSummary? pending = null;
                if (pendingByPartId.TryGetValue(r.Id, out var pr))
                {
                    pending = new PendingWorkflowSummary(
                        pr.Id, pr.DefinitionId, pr.CurrentStepId, pr.Mode, pr.LastActivityAt);
                }
                return new PartListResponseModel(
                    r.Id,
                    r.PartNumber,
                    r.Name,
                    r.Description,
                    r.Revision,
                    r.Status,
                    r.ProcurementSource,
                    r.InventoryClass,
                    r.BomEntryCount,
                    r.CreatedAt,
                    price.UnitPrice,
                    price.Currency,
                    price.Source.ToString(),
                    pending);
            })
            .ToList();

        return new PagedResponse<PartListResponseModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<PartDetailResponseModel?> GetDetailAsync(int id, CancellationToken ct)
    {
        var part = await db.Parts
            .Include(p => p.BOMEntries).ThenInclude(b => b.ChildPart)
            .Include(p => p.UsedInBOM).ThenInclude(b => b.ParentPart)
            .Include(p => p.PreferredVendor)
            .Include(p => p.ToolingAsset)
            .Include(p => p.ItemKind)
            .Include(p => p.MaterialSpec)
            .Include(p => p.ValuationClass)
            // Pillar 4 Phase 2 — UoM cluster needs resolved code/label fields
            // on the detail response so the frontend doesn't have to round-trip.
            .Include(p => p.StockUom)
            .Include(p => p.PurchaseUom)
            .Include(p => p.SalesUom)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (part is null)
            return null;

        var bomEntries = part.BOMEntries
            .OrderBy(b => b.SortOrder)
            .Select(b => new BOMEntryResponseModel(
                b.Id,
                b.ChildPartId,
                b.ChildPart.PartNumber,
                b.ChildPart.Name,
                b.Quantity,
                b.ReferenceDesignator,
                b.SortOrder,
                b.SourceType,
                b.LeadTimeDays,
                b.Notes))
            .ToList();

        var usedIn = part.UsedInBOM
            .OrderBy(b => b.ParentPart.PartNumber)
            .Select(b => new BOMUsageResponseModel(
                b.Id,
                b.ParentPartId,
                b.ParentPart.PartNumber,
                b.ParentPart.Name,
                b.Quantity))
            .ToList();

        var resolved = await pricingResolver.ResolveAsync(part.Id, customerId: null, quantity: null, ct);

        return new PartDetailResponseModel(
            part.Id,
            part.PartNumber,
            part.Name,
            part.Description,
            part.Revision,
            part.Status,
            // Pillar 1 — Decomposed type axes
            part.ProcurementSource,
            part.InventoryClass,
            part.ItemKindId,
            part.ItemKind?.Label,
            // Tier 0 additions
            part.TraceabilityType,
            part.AbcClass,
            // Pillar 2 — Tier 2 material spec FK
            part.MaterialSpecId,
            part.MaterialSpec?.Label,
            part.ExternalId,
            part.ExternalRef,
            part.Provider,
            part.PreferredVendorId,
            part.PreferredVendor?.CompanyName,
            part.MinStockThreshold,
            part.ReorderPoint,
            part.ReorderQuantity,
            part.SafetyStockDays,
            part.ToolingAssetId,
            part.ToolingAsset?.Name,
            // Workflow Pattern Phase 5 — surfaces cost gates for the hasCost predicate.
            part.ManualCostOverride,
            part.CurrentCostCalculationId,
            // Pillar 2 — Tier 2 measurement profile
            part.WeightEach,
            part.WeightDisplayUnit,
            part.LengthMm,
            part.WidthMm,
            part.HeightMm,
            part.DimensionDisplayUnit,
            part.VolumeMl,
            part.VolumeDisplayUnit,
            // Pillar 2 — Tier 2 valuation
            part.ValuationClassId,
            part.ValuationClass?.Label,
            // Pillar 2 — Tier 3 compliance + classification
            part.HtsCode,
            part.HazmatClass,
            part.ShelfLifeDays,
            part.BackflushPolicy,
            part.IsKit,
            part.IsConfigurable,
            part.DefaultBinId,
            part.SourcePartId,
            bomEntries,
            usedIn,
            part.CreatedAt,
            part.UpdatedAt,
            // Pillar 4 Phase 2 — UoM cluster (id + resolved code/label)
            part.StockUomId,
            part.StockUom?.Code,
            part.StockUom?.Name,
            part.PurchaseUomId,
            part.PurchaseUom?.Code,
            part.PurchaseUom?.Name,
            part.SalesUomId,
            part.SalesUom?.Code,
            part.SalesUom?.Name,
            // Pillar 4 Phase 2 — MRP cluster
            part.IsMrpPlanned,
            part.LotSizingRule,
            part.FixedOrderQuantity,
            part.MinimumOrderQuantity,
            part.OrderMultiple,
            part.PlanningFenceDays,
            part.DemandFenceDays,
            // Pillar 4 Phase 2 — Quality cluster (receiving inspection)
            part.RequiresReceivingInspection,
            part.ReceivingInspectionTemplateId,
            part.InspectionFrequency,
            part.InspectionSkipAfterN,
            resolved.UnitPrice,
            resolved.Currency,
            resolved.Source.ToString());
    }

    public Task<Part?> FindAsync(int id, CancellationToken ct)
        => db.Parts.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> PartNumberExistsAsync(string partNumber, int? excludeId, CancellationToken ct)
    {
        var query = db.Parts.Where(p => p.PartNumber == partNumber);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task<string> GetNextPartNumberAsync(InventoryClass inventoryClass, CancellationToken ct)
    {
        // Pre-beta: numbering prefix is now driven by the InventoryClass axis
        // (replacing the legacy single PartType enum). The 11 viable
        // procurement × inventory combos collapse onto inventory-class for
        // numbering — most prefixes are unchanged from the legacy mapping.
        var prefix = inventoryClass switch
        {
            InventoryClass.Raw => "RAW-",
            InventoryClass.Component => "PRT-",
            InventoryClass.Subassembly => "ASM-",
            InventoryClass.FinishedGood => "FG-",
            InventoryClass.Consumable => "CON-",
            InventoryClass.Tool => "TLG-",
            _ => "PRT-",
        };

        // IgnoreQueryFilters() so soft-deleted parts are also considered when
        // computing the next sequential number. The unique index ix_parts_part_number
        // covers ALL rows (including soft-deleted), so reusing a soft-deleted
        // suffix would 23505 the INSERT. (Phase 3 F6.)
        var suffixes = await db.Parts
            .IgnoreQueryFilters()
            .Where(p => p.PartNumber.StartsWith(prefix))
            .Select(p => p.PartNumber.Substring(prefix.Length))
            .ToListAsync(ct);

        var maxNumber = suffixes
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{maxNumber + 1:D5}";
    }

    public async Task AddAsync(Part part, CancellationToken ct)
    {
        await db.Parts.AddAsync(part, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<BOMEntry?> FindBomEntryAsync(int bomEntryId, int parentPartId, CancellationToken ct)
        => db.BOMEntries.FirstOrDefaultAsync(b => b.Id == bomEntryId && b.ParentPartId == parentPartId, ct);

    public async Task<int> GetMaxBomSortOrderAsync(int parentPartId, CancellationToken ct)
    {
        var max = await db.BOMEntries
            .Where(b => b.ParentPartId == parentPartId)
            .MaxAsync(b => (int?)b.SortOrder, ct);
        return max ?? 0;
    }

    public async Task AddBomEntryAsync(BOMEntry entry, CancellationToken ct)
    {
        await db.BOMEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task RemoveBomEntryAsync(BOMEntry entry)
    {
        db.BOMEntries.Remove(entry);
        return db.SaveChangesAsync(default);
    }

    public async Task<List<OperationResponseModel>> GetOperationsAsync(int partId, CancellationToken ct)
    {
        return await db.Operations
            .Where(s => s.PartId == partId)
            .Include(s => s.WorkCenter)
            .Include(s => s.ReferencedOperation)
            .Include(s => s.SubcontractVendor)
            .Include(s => s.Materials).ThenInclude(m => m.BomEntry).ThenInclude(b => b.ChildPart)
            .OrderBy(s => s.StepNumber)
            .Select(s => new OperationResponseModel(
                s.Id,
                s.PartId,
                s.StepNumber,
                s.Title,
                s.Instructions,
                s.WorkCenterId,
                s.WorkCenter != null ? s.WorkCenter.Name : null,
                s.EstimatedMinutes,
                s.IsQcCheckpoint,
                s.QcCriteria,
                s.ReferencedOperationId,
                s.ReferencedOperation != null ? $"Op {s.ReferencedOperation.StepNumber}: {s.ReferencedOperation.Title}" : null,
                s.Materials.Select(m => new OperationMaterialResponseModel(
                    m.Id,
                    m.OperationId,
                    m.BomEntryId,
                    m.BomEntry.ChildPart.PartNumber,
                    m.BomEntry.ChildPart.Name,
                    m.Quantity,
                    m.Notes)).ToList(),
                s.CreatedAt,
                s.UpdatedAt,
                // Phase 3 H5 / WU-13 — subcontract metadata round-tripped.
                s.IsSubcontract,
                s.SubcontractVendorId,
                s.SubcontractVendor != null ? s.SubcontractVendor.CompanyName : null,
                s.SubcontractTurnTimeDays))
            .ToListAsync(ct);
    }

    public Task<Operation?> FindOperationAsync(int operationId, CancellationToken ct)
        => db.Operations.FirstOrDefaultAsync(s => s.Id == operationId, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}

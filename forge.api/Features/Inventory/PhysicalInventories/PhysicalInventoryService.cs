using System.Globalization;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Forge.Data.Interceptors;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

/// <inheritdoc />
public sealed class PhysicalInventoryService(
    AppDbContext db,
    IInventoryAdjustmentPostingService posting,
    IClock clock) : IPhysicalInventoryService
{
    internal const string TagSourceType = "PhysicalInventoryTag";

    private static readonly PhysicalInventoryStatus[] ActiveStatuses =
        [PhysicalInventoryStatus.Frozen, PhysicalInventoryStatus.Counting, PhysicalInventoryStatus.Review];

    private sealed record LocationNode(int Id, int? ParentId, string Name, string? Barcode);

    public async Task<IReadOnlyList<PhysicalInventoryResponseModel>> ListAsync(
        PhysicalInventoryStatus? status, CancellationToken ct = default)
    {
        var query = db.PhysicalInventories.AsNoTracking().Include(p => p.ScopeLocation).AsQueryable();
        if (status is not null)
            query = query.Where(p => p.Status == status);

        var inventories = await query.OrderByDescending(p => p.Id).Take(200).ToListAsync(ct);
        var ids = inventories.Select(p => p.Id).ToList();
        var counts = await db.PhysicalInventoryTags.AsNoTracking()
            .Where(t => ids.Contains(t.PhysicalInventoryId) && t.Status != PhysicalInventoryTagStatus.Void)
            .GroupBy(t => t.PhysicalInventoryId)
            .Select(g => new
            {
                Id = g.Key,
                Total = g.Count(),
                Counted = g.Count(t => t.Status == PhysicalInventoryTagStatus.Counted || t.Status == PhysicalInventoryTagStatus.Recounted),
                Recount = g.Count(t => t.Status == PhysicalInventoryTagStatus.RecountRequired),
            })
            .ToDictionaryAsync(c => c.Id, ct);

        return inventories.Select(p =>
        {
            counts.TryGetValue(p.Id, out var c);
            return Summary(p, c?.Total ?? 0, c?.Counted ?? 0, c?.Recount ?? 0);
        }).ToList();
    }

    public async Task<PhysicalInventoryDetailResponseModel> GetAsync(int id, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.AsNoTracking()
            .Include(p => p.ScopeLocation)
            .Include(p => p.Tags).ThenInclude(t => t.Part)
            .Include(p => p.Tags).ThenInclude(t => t.Location)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        return Detail(inventory);
    }

    public async Task<PhysicalInventoryTagResponseModel> FindTagAsync(int id, int tagNumber, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        var tag = await db.PhysicalInventoryTags.AsNoTracking()
            .Include(t => t.Part).Include(t => t.Location)
            .FirstOrDefaultAsync(t => t.PhysicalInventoryId == id && t.TagNumber == tagNumber, ct)
            ?? throw new KeyNotFoundException($"Tag {tagNumber} is not on physical inventory {inventory.InventoryNumber}");
        return TagModel(tag, BookVisible(inventory));
    }

    public async Task<PhysicalInventoryDetailResponseModel> CreateAsync(
        CreatePhysicalInventoryRequestModel request, int userId, CancellationToken ct = default)
    {
        if (request.ScopeLocationId is int scopeId
            && !await db.StorageLocations.AnyAsync(l => l.Id == scopeId && l.IsActive, ct))
            throw new KeyNotFoundException($"Location {scopeId} not found");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.physical-inventory.number", string.Empty, ct);

        var inventory = new PhysicalInventory
        {
            InventoryNumber = await NextNumberAsync(ct),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ScopeLocationId = request.ScopeLocationId,
            CountDate = request.CountDate,
            BlindCount = request.BlindCount,
            RecountTolerancePercent = request.RecountTolerancePercent,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedByUserId = userId,
        };
        db.PhysicalInventories.Add(inventory);
        await db.SaveChangesAsync(ct);
        db.LogActivityAt("created", $"Created physical inventory {inventory.InventoryNumber} for {inventory.CountDate:MM/dd/yyyy}",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await GetAsync(inventory.Id, ct);
    }

    public async Task<PhysicalInventoryDetailResponseModel> FreezeAsync(int id, int userId, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.physical-inventory.freeze", string.Empty, ct);

        var inventory = await db.PhysicalInventories.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        if (inventory.Status != PhysicalInventoryStatus.Draft)
            throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} is {inventory.Status}; only a draft can be frozen.");

        var locations = await LocationsAsync(ct);
        var others = await db.PhysicalInventories.AsNoTracking()
            .Where(p => p.Id != id && ActiveStatuses.Contains(p.Status))
            .Select(p => new { p.InventoryNumber, p.ScopeLocationId })
            .ToListAsync(ct);
        var overlapping = others.FirstOrDefault(o => Overlaps(o.ScopeLocationId, inventory.ScopeLocationId, locations));
        if (overlapping is not null)
            throw new InvalidOperationException(
                $"Physical inventory {overlapping.InventoryNumber} already has stock in this scope frozen. Post or cancel it first.");

        var scope = ScopeIds(inventory.ScopeLocationId, locations);
        var rows = await db.BinContents.AsNoTracking()
            .Where(c => c.EntityType == "part" && c.RemovedAt == null && c.Quantity > 0m && c.OwnerVendorId == null)
            .Select(c => new { c.Id, c.LocationId, c.EntityId, c.LotNumber, c.Quantity })
            .ToListAsync(ct);
        rows = rows.Where(r => scope is null || scope.Contains(r.LocationId)).ToList();

        var partIds = rows.Select(r => r.EntityId).Distinct().ToList();
        var partNumbers = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
        var paths = locations.ToDictionary(l => l.Key, l => PathOf(l.Key, locations));

        var now = clock.UtcNow;
        var number = 0;
        foreach (var row in rows
                     .OrderBy(r => paths.GetValueOrDefault(r.LocationId, string.Empty), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => partNumbers.GetValueOrDefault(r.EntityId, string.Empty), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => r.LotNumber)
                     .ThenBy(r => r.Id))
        {
            db.PhysicalInventoryTags.Add(new PhysicalInventoryTag
            {
                PhysicalInventoryId = inventory.Id,
                TagNumber = ++number,
                LocationId = row.LocationId,
                BinContentId = row.Id,
                PartId = row.EntityId,
                LotNumber = row.LotNumber,
                FrozenQuantity = row.Quantity,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        inventory.Status = PhysicalInventoryStatus.Frozen;
        inventory.FrozenAt = now;
        inventory.FrozenByUserId = userId;
        db.LogActivityAt("frozen", $"Froze stock for physical inventory {inventory.InventoryNumber}: {number} tag(s)",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PhysicalInventoryTagResponseModel> AddTagAsync(
        int id, AddPhysicalInventoryTagRequestModel request, int userId, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("inventory.physical-inventory.tags", id.ToString(CultureInfo.InvariantCulture), ct);

        var inventory = await CountableAsync(id, ct);
        var locations = await LocationsAsync(ct);
        if (!locations.ContainsKey(request.LocationId))
            throw new KeyNotFoundException($"Location {request.LocationId} not found");
        var scope = ScopeIds(inventory.ScopeLocationId, locations);
        if (scope is not null && !scope.Contains(request.LocationId))
            throw new InvalidOperationException("That location is outside this physical inventory's scope.");
        if (!await db.Parts.AnyAsync(p => p.Id == request.PartId, ct))
            throw new KeyNotFoundException($"Part {request.PartId} not found");

        var lot = string.IsNullOrWhiteSpace(request.LotNumber) ? null : request.LotNumber.Trim();
        var serial = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();
        var duplicate = await db.PhysicalInventoryTags.AsNoTracking().FirstOrDefaultAsync(t =>
            t.PhysicalInventoryId == id && t.Status != PhysicalInventoryTagStatus.Void
            && t.LocationId == request.LocationId && t.PartId == request.PartId && t.LotNumber == lot, ct);
        if (duplicate is not null)
            throw new InvalidOperationException($"Tag {duplicate.TagNumber} already covers this part, lot and location; count that tag.");

        var next = (await db.PhysicalInventoryTags.Where(t => t.PhysicalInventoryId == id)
            .MaxAsync(t => (int?)t.TagNumber, ct) ?? 0) + 1;
        var now = clock.UtcNow;
        var tag = new PhysicalInventoryTag
        {
            PhysicalInventoryId = id,
            TagNumber = next,
            LocationId = request.LocationId,
            PartId = request.PartId,
            LotNumber = lot,
            SerialNumber = serial,
            FrozenQuantity = 0m,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PhysicalInventoryTags.Add(tag);
        if (request.Quantity is decimal quantity)
            ApplyCount(inventory, tag, quantity, null, userId, now);
        db.LogActivityAt("tag-added", $"Added tag {next} for stock found during {inventory.InventoryNumber}",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await FindTagAsync(id, next, ct);
    }

    public async Task<PhysicalInventoryTagResponseModel> EnterCountAsync(
        int id, int tagId, EnterPhysicalCountRequestModel request, int userId, CancellationToken ct = default)
    {
        var inventory = await CountableAsync(id, ct);
        var tag = await db.PhysicalInventoryTags.FirstOrDefaultAsync(t => t.Id == tagId && t.PhysicalInventoryId == id, ct)
            ?? throw new KeyNotFoundException($"Tag {tagId} is not on physical inventory {inventory.InventoryNumber}");
        if (tag.Status == PhysicalInventoryTagStatus.Void)
            throw new InvalidOperationException($"Tag {tag.TagNumber} is void.");

        var recount = tag.Status is PhysicalInventoryTagStatus.RecountRequired or PhysicalInventoryTagStatus.Recounted;
        ApplyCount(inventory, tag, request.Quantity, request.Notes, userId, clock.UtcNow);
        db.LogActivityAt(recount ? "recounted" : "counted",
            $"Tag {tag.TagNumber} {(recount ? "recounted" : "counted")} at {request.Quantity.ToString("0.####", CultureInfo.InvariantCulture)}",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);
        return await FindTagAsync(id, tag.TagNumber, ct);
    }

    public async Task<PhysicalInventoryDetailResponseModel> SubmitForReviewAsync(int id, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        if (inventory.Status is not (PhysicalInventoryStatus.Frozen or PhysicalInventoryStatus.Counting))
            throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} is {inventory.Status}; only a count in progress can go to review.");

        var live = inventory.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void).ToList();
        var open = live.Count(t => t.Status == PhysicalInventoryTagStatus.Open);
        var recount = live.Count(t => t.Status == PhysicalInventoryTagStatus.RecountRequired);
        if (open > 0 || recount > 0)
            throw new InvalidOperationException(
                $"Physical inventory {inventory.InventoryNumber} still has {open} uncounted tag(s) and {recount} awaiting a recount.");

        inventory.Status = PhysicalInventoryStatus.Review;
        db.LogActivityAt("review-started", $"Physical inventory {inventory.InventoryNumber} sent to variance review",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PhysicalInventoryDetailResponseModel> ReopenCountingAsync(int id, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        if (inventory.Status != PhysicalInventoryStatus.Review)
            throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} is {inventory.Status}; only one in review can go back to counting.");

        inventory.Status = PhysicalInventoryStatus.Counting;
        db.LogActivityAt("counting-reopened", $"Physical inventory {inventory.InventoryNumber} sent back to counting",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PhysicalInventoryVarianceResponseModel> GetVarianceAsync(int id, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.AsNoTracking()
            .Include(p => p.Tags).ThenInclude(t => t.Part)
            .Include(p => p.Tags).ThenInclude(t => t.Location)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");

        var live = inventory.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void).ToList();
        var costs = await UnitCostsAsync(live.Select(t => t.PartId).Distinct().ToList(), ct);
        var lines = live
            .Where(t => t.FinalQuantity is not null)
            .Select(t =>
            {
                var counted = t.FinalQuantity!.Value;
                var variance = counted - t.FrozenQuantity;
                var unitCost = inventory.Status == PhysicalInventoryStatus.Posted && t.UnitCost is decimal posted
                    ? posted
                    : costs.GetValueOrDefault(t.PartId);
                return new PhysicalInventoryVarianceLineResponseModel
                {
                    TagId = t.Id,
                    TagNumber = t.TagNumber,
                    PartId = t.PartId,
                    PartNumber = t.Part.PartNumber,
                    PartDescription = t.Part.Description ?? t.Part.Name,
                    LocationName = t.Location.Name,
                    LotNumber = t.LotNumber,
                    FrozenQuantity = t.FrozenQuantity,
                    CountedQuantity = counted,
                    VarianceQuantity = variance,
                    VariancePercent = t.FrozenQuantity == 0m
                        ? null
                        : Math.Round(variance / t.FrozenQuantity * 100m, 2, MidpointRounding.AwayFromZero),
                    UnitCost = unitCost,
                    VarianceValue = t.VarianceValue ?? Math.Round(variance * unitCost, 2, MidpointRounding.AwayFromZero),
                    Recounted = t.RecountQuantity is not null,
                };
            })
            .OrderByDescending(l => Math.Abs(l.VarianceValue))
            .ThenBy(l => l.TagNumber)
            .ToList();

        return new PhysicalInventoryVarianceResponseModel
        {
            PhysicalInventoryId = inventory.Id,
            InventoryNumber = inventory.InventoryNumber,
            Lines = lines,
            TagCount = live.Count,
            VarianceTagCount = lines.Count(l => l.VarianceQuantity != 0m),
            ShortageValue = lines.Where(l => l.VarianceValue < 0m).Sum(l => l.VarianceValue),
            OverageValue = lines.Where(l => l.VarianceValue > 0m).Sum(l => l.VarianceValue),
            NetVarianceValue = lines.Sum(l => l.VarianceValue),
        };
    }

    public async Task<PhysicalInventoryDetailResponseModel> PostAsync(int id, int userId, CancellationToken ct = default)
    {
        using (PhysicalInventoryFreeze.Suspend())
        {
            await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
            await db.AcquireAdvisoryLockAsync("inventory.physical-inventory.post", id.ToString(CultureInfo.InvariantCulture), ct);

            var inventory = await db.PhysicalInventories.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
            if (inventory.Status != PhysicalInventoryStatus.Review)
                throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} is {inventory.Status}; only a reviewed count can be posted.");

            var live = inventory.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void).OrderBy(t => t.TagNumber).ToList();
            if (live.Any(t => t.FinalQuantity is null))
                throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} has uncounted tags.");

            var costs = await UnitCostsAsync(live.Select(t => t.PartId).Distinct().ToList(), ct);
            var now = clock.UtcNow;
            var adjustments = new List<(PhysicalInventoryTag Tag, BinMovement Movement, decimal Delta)>();

            foreach (var tag in live)
            {
                var delta = tag.FinalQuantity!.Value - tag.FrozenQuantity;
                var unitCost = costs.GetValueOrDefault(tag.PartId);
                tag.UnitCost = unitCost;
                tag.VarianceValue = Math.Round(delta * unitCost, 2, MidpointRounding.AwayFromZero);
                tag.UpdatedAt = now;
                if (delta == 0m)
                    continue;

                BinContent? content = tag.BinContentId is int binId
                    ? await db.BinContents.FirstOrDefaultAsync(c => c.Id == binId, ct)
                    : null;
                if (content is not null)
                {
                    var onHand = content.RemovedAt is null ? content.Quantity : 0m;
                    if (onHand != tag.FrozenQuantity)
                        throw new InvalidOperationException(
                            $"Tag {tag.TagNumber}: the bin changed from {tag.FrozenQuantity:0.####} to {onHand:0.####} after the freeze. Cancel and count again.");
                    if (tag.FinalQuantity.Value < content.ReservedQuantity)
                        throw new InvalidOperationException(
                            $"Tag {tag.TagNumber}: counted {tag.FinalQuantity.Value:0.####} but {content.ReservedQuantity:0.####} is reserved. Release the reservation first.");
                    content.Quantity = tag.FinalQuantity.Value;
                    content.RemovedAt = content.Quantity == 0m ? now : null;
                    content.RemovedBy = content.Quantity == 0m ? userId : null;
                }
                else if (delta > 0m)
                {
                    var added = await ScanBinStock.AddAsync(db, tag.PartId, tag.LocationId, tag.LotNumber, delta, userId, now, ct);
                    tag.BinContent = added;
                }
                else
                {
                    throw new InvalidOperationException($"Tag {tag.TagNumber}: the bin it counted no longer exists. Cancel and count again.");
                }

                var movement = new BinMovement
                {
                    EntityType = "part",
                    EntityId = tag.PartId,
                    Quantity = Math.Abs(delta),
                    LotNumber = tag.LotNumber,
                    FromLocationId = delta < 0m ? tag.LocationId : null,
                    ToLocationId = delta > 0m ? tag.LocationId : null,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.PhysicalInventory,
                    PhysicalInventoryId = inventory.Id,
                    Notes = $"Physical inventory {inventory.InventoryNumber} tag {tag.TagNumber}",
                };
                db.BinMovements.Add(movement);
                adjustments.Add((tag, movement, delta));
            }

            await db.SaveChangesAsync(ct);
            foreach (var (tag, movement, delta) in adjustments)
            {
                tag.BinMovementId = movement.Id;
                await posting.PostAdjustmentAsync(new InventoryAdjustmentPosting(
                    TagSourceType, tag.Id, tag.PartId, delta, inventory.CountDate, userId,
                    $"physical inventory {inventory.InventoryNumber} tag {tag.TagNumber}"), ct);
            }

            inventory.Status = PhysicalInventoryStatus.Posted;
            inventory.PostedAt = now;
            inventory.PostedByUserId = userId;
            inventory.TotalVarianceValue = live.Sum(t => t.VarianceValue ?? 0m);
            db.LogActivityAt("posted",
                $"Posted physical inventory {inventory.InventoryNumber}: {adjustments.Count} adjustment(s), net {inventory.TotalVarianceValue:0.00}",
                ("PhysicalInventory", inventory.Id));
            await db.SaveChangesAsync(ct);

            if (tx is not null)
                await tx.CommitAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<PhysicalInventoryDetailResponseModel> CancelAsync(int id, int userId, CancellationToken ct = default)
    {
        var inventory = await db.PhysicalInventories.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        if (inventory.Status is PhysicalInventoryStatus.Posted or PhysicalInventoryStatus.Cancelled)
            throw new InvalidOperationException($"Physical inventory {inventory.InventoryNumber} is already {inventory.Status}.");

        inventory.Status = PhysicalInventoryStatus.Cancelled;
        inventory.CancelledAt = clock.UtcNow;
        inventory.CancelledByUserId = userId;
        db.LogActivityAt("cancelled", $"Cancelled physical inventory {inventory.InventoryNumber}; stock released",
            ("PhysicalInventory", inventory.Id));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<(byte[] Content, string FileName)> BuildCountSheetsAsync(
        int id, bool? blind, string? language, CancellationToken ct = default)
    {
        var detail = await GetAsync(id, ct);
        if (detail.Status == PhysicalInventoryStatus.Draft)
            throw new InvalidOperationException("Freeze the physical inventory first; the count sheets list its tags.");

        var hideBook = detail.BlindCount || blind == true;
        var spanish = language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true;
        var companyName = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == "company.name" || s.Key == "company_name")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var document = new PhysicalInventoryCountSheetDocument(detail, hideBook, spanish, companyName);
        return (document.GeneratePdf(), $"{detail.InventoryNumber}-count-sheets.pdf");
    }

    private static void ApplyCount(
        PhysicalInventory inventory, PhysicalInventoryTag tag, decimal quantity, string? notes, int userId, DateTimeOffset now)
    {
        if (quantity < 0m)
            throw new InvalidOperationException("A counted quantity cannot be negative.");

        if (tag.Status is PhysicalInventoryTagStatus.RecountRequired or PhysicalInventoryTagStatus.Recounted)
        {
            tag.RecountQuantity = quantity;
            tag.RecountedByUserId = userId;
            tag.RecountedAt = now;
            tag.Status = PhysicalInventoryTagStatus.Recounted;
        }
        else
        {
            tag.CountedQuantity = quantity;
            tag.CountedByUserId = userId;
            tag.CountedAt = now;
            tag.Status = NeedsRecount(tag.FrozenQuantity, quantity, inventory.RecountTolerancePercent)
                ? PhysicalInventoryTagStatus.RecountRequired
                : PhysicalInventoryTagStatus.Counted;
        }

        if (!string.IsNullOrWhiteSpace(notes))
            tag.Notes = notes.Trim();
        tag.UpdatedAt = now;
        if (inventory.Status == PhysicalInventoryStatus.Frozen)
            inventory.Status = PhysicalInventoryStatus.Counting;
    }

    /// <summary>
    /// A count asks for a recount when it is off the book quantity by more than the tolerance percent. A tolerance
    /// of zero turns recounts off; stock found where the book had none always asks for one when recounts are on.
    /// </summary>
    internal static bool NeedsRecount(decimal frozen, decimal counted, decimal tolerancePercent)
    {
        if (tolerancePercent <= 0m || frozen == counted)
            return false;
        if (frozen == 0m)
            return true;
        return Math.Abs(counted - frozen) / frozen * 100m > tolerancePercent;
    }

    private async Task<PhysicalInventory> CountableAsync(int id, CancellationToken ct)
    {
        var inventory = await db.PhysicalInventories.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Physical inventory {id} not found");
        if (inventory.Status is not (PhysicalInventoryStatus.Frozen or PhysicalInventoryStatus.Counting))
            throw new InvalidOperationException(
                $"Physical inventory {inventory.InventoryNumber} is {inventory.Status}; counts are taken while it is frozen or counting.");
        return inventory;
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var next = await db.PhysicalInventories.IgnoreQueryFilters().CountAsync(ct) + 1;
        while (true)
        {
            var candidate = $"PI-{next:D6}";
            if (!await db.PhysicalInventories.IgnoreQueryFilters().AnyAsync(p => p.InventoryNumber == candidate, ct))
                return candidate;
            next++;
        }
    }

    private async Task<Dictionary<int, LocationNode>> LocationsAsync(CancellationToken ct)
        => await db.StorageLocations.AsNoTracking()
            .Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.Barcode))
            .ToDictionaryAsync(l => l.Id, ct);

    private static HashSet<int>? ScopeIds(int? scopeLocationId, IReadOnlyDictionary<int, LocationNode> locations)
    {
        if (scopeLocationId is not int scopeId)
            return null;
        return locations.Keys.Where(id => IsWithin(id, scopeId, locations)).ToHashSet();
    }

    private static bool Overlaps(int? a, int? b, IReadOnlyDictionary<int, LocationNode> locations)
        => a is null || b is null || IsWithin(a.Value, b.Value, locations) || IsWithin(b.Value, a.Value, locations);

    private static bool IsWithin(int locationId, int scopeId, IReadOnlyDictionary<int, LocationNode> locations)
    {
        int? current = locationId;
        var guard = 0;
        while (current is int id && guard++ < 64)
        {
            if (id == scopeId)
                return true;
            current = locations.TryGetValue(id, out var node) ? node.ParentId : null;
        }
        return false;
    }

    private static string PathOf(int locationId, IReadOnlyDictionary<int, LocationNode> locations)
    {
        var names = new List<string>();
        int? current = locationId;
        var guard = 0;
        while (current is int id && guard++ < 64 && locations.TryGetValue(id, out var node))
        {
            names.Insert(0, node.Name);
            current = node.ParentId;
        }
        return string.Join(" / ", names);
    }

    private async Task<Dictionary<int, decimal>> UnitCostsAsync(IReadOnlyList<int> partIds, CancellationToken ct)
    {
        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
        var store = bookId is int book
            ? await db.InventoryValuations.AsNoTracking()
                .Where(v => v.BookId == book && partIds.Contains(v.PartId))
                .ToDictionaryAsync(v => v.PartId, v => v.AverageUnitCost, ct)
            : [];
        var standards = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, Cost = p.ManualCostOverride ?? (p.CurrentCostCalculation != null ? p.CurrentCostCalculation.ResultAmount : 0m) })
            .ToDictionaryAsync(p => p.Id, p => p.Cost, ct);
        return partIds.ToDictionary(
            id => id,
            id => store.TryGetValue(id, out var average) && average > 0m ? average : standards.GetValueOrDefault(id));
    }

    private static bool BookVisible(PhysicalInventory inventory)
        => !inventory.BlindCount
            || inventory.Status is PhysicalInventoryStatus.Review or PhysicalInventoryStatus.Posted or PhysicalInventoryStatus.Cancelled;

    private static PhysicalInventoryResponseModel Summary(PhysicalInventory p, int total, int counted, int recount) => new()
    {
        Id = p.Id,
        InventoryNumber = p.InventoryNumber,
        Description = p.Description,
        ScopeLocationId = p.ScopeLocationId,
        ScopeLocationName = p.ScopeLocation?.Name,
        Status = p.Status,
        CountDate = p.CountDate,
        BlindCount = p.BlindCount,
        RecountTolerancePercent = p.RecountTolerancePercent,
        Notes = p.Notes,
        FrozenAt = p.FrozenAt,
        PostedAt = p.PostedAt,
        CancelledAt = p.CancelledAt,
        TotalVarianceValue = p.TotalVarianceValue,
        TagCount = total,
        CountedTagCount = counted,
        RecountTagCount = recount,
        BookQuantitiesVisible = BookVisible(p),
        CreatedAt = p.CreatedAt,
    };

    private static PhysicalInventoryDetailResponseModel Detail(PhysicalInventory p)
    {
        var live = p.Tags.Where(t => t.Status != PhysicalInventoryTagStatus.Void).ToList();
        var summary = Summary(p, live.Count,
            live.Count(t => t.Status is PhysicalInventoryTagStatus.Counted or PhysicalInventoryTagStatus.Recounted),
            live.Count(t => t.Status == PhysicalInventoryTagStatus.RecountRequired));
        var showBook = BookVisible(p);
        return new PhysicalInventoryDetailResponseModel
        {
            Id = summary.Id,
            InventoryNumber = summary.InventoryNumber,
            Description = summary.Description,
            ScopeLocationId = summary.ScopeLocationId,
            ScopeLocationName = summary.ScopeLocationName,
            Status = summary.Status,
            CountDate = summary.CountDate,
            BlindCount = summary.BlindCount,
            RecountTolerancePercent = summary.RecountTolerancePercent,
            Notes = summary.Notes,
            FrozenAt = summary.FrozenAt,
            PostedAt = summary.PostedAt,
            CancelledAt = summary.CancelledAt,
            TotalVarianceValue = summary.TotalVarianceValue,
            TagCount = summary.TagCount,
            CountedTagCount = summary.CountedTagCount,
            RecountTagCount = summary.RecountTagCount,
            BookQuantitiesVisible = summary.BookQuantitiesVisible,
            CreatedAt = summary.CreatedAt,
            Tags = p.Tags.OrderBy(t => t.TagNumber).Select(t => TagModel(t, showBook)).ToList(),
        };
    }

    private static PhysicalInventoryTagResponseModel TagModel(PhysicalInventoryTag t, bool showBook) => new()
    {
        Id = t.Id,
        PhysicalInventoryId = t.PhysicalInventoryId,
        TagNumber = t.TagNumber,
        LocationId = t.LocationId,
        LocationName = t.Location?.Name ?? string.Empty,
        LocationBarcode = t.Location?.Barcode,
        PartId = t.PartId,
        PartNumber = t.Part?.PartNumber ?? string.Empty,
        PartDescription = t.Part is null ? null : t.Part.Description ?? t.Part.Name,
        LotNumber = t.LotNumber,
        SerialNumber = t.SerialNumber,
        FrozenQuantity = showBook ? t.FrozenQuantity : null,
        CountedQuantity = t.CountedQuantity,
        RecountQuantity = t.RecountQuantity,
        FinalQuantity = t.FinalQuantity,
        Status = t.Status,
        CountedAt = t.CountedAt,
        RecountedAt = t.RecountedAt,
        Notes = t.Notes,
    };
}

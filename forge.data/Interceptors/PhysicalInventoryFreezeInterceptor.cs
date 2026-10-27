using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Data.Interceptors;

/// <summary>
/// Blocks stock movements in the scope of a frozen physical inventory (status Frozen, Counting or Review): a save
/// that adds a part movement from or to a frozen location, or adds, removes or changes the quantity or location of a
/// part's bin content there, is refused with <see cref="PhysicalInventoryFrozenException"/>. A scope is the
/// inventory's location and every location under it, or every location when the inventory names none. Receipts to a
/// location outside every frozen scope (a staging bin) pass. The inventory's own posting and cancel run inside
/// <see cref="PhysicalInventoryFreeze.Suspend"/>.
/// </summary>
public sealed class PhysicalInventoryFreezeInterceptor : SaveChangesInterceptor
{
    /// <summary>The one shared instance every context registers, so EF keeps reusing its internal service provider.</summary>
    public static readonly PhysicalInventoryFreezeInterceptor Instance = new();

    private static readonly PhysicalInventoryStatus[] FrozenStatuses =
        [PhysicalInventoryStatus.Frozen, PhysicalInventoryStatus.Counting, PhysicalInventoryStatus.Review];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnforceAsync(eventData.Context, sync: true, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await EnforceAsync(eventData.Context, sync: false, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static async Task EnforceAsync(DbContext? context, bool sync, CancellationToken ct)
    {
        if (context is null || PhysicalInventoryFreeze.IsSuspended)
            return;

        var touched = TouchedLocations(context);
        if (touched.Count == 0)
            return;

        var frozenQuery = context.Set<PhysicalInventory>().AsNoTracking()
            .Where(p => FrozenStatuses.Contains(p.Status))
            .Select(p => new { p.InventoryNumber, p.ScopeLocationId });
        var frozen = sync ? frozenQuery.ToList() : await frozenQuery.ToListAsync(ct);
        if (frozen.Count == 0)
            return;

        var everywhere = frozen.FirstOrDefault(p => p.ScopeLocationId is null);
        if (everywhere is not null)
            throw Refusal(everywhere.InventoryNumber);

        var locationQuery = context.Set<StorageLocation>().AsNoTracking()
            .IgnoreQueryFilters()
            .Select(l => new { l.Id, l.ParentId });
        var locations = sync ? locationQuery.ToList() : await locationQuery.ToListAsync(ct);
        var parents = locations.ToDictionary(l => l.Id, l => l.ParentId);

        foreach (var locationId in touched)
        {
            var hit = frozen.FirstOrDefault(p => IsWithin(locationId, p.ScopeLocationId!.Value, parents));
            if (hit is not null)
                throw Refusal(hit.InventoryNumber);
        }
    }

    private static HashSet<int> TouchedLocations(DbContext context)
    {
        var touched = new HashSet<int>();

        foreach (var entry in context.ChangeTracker.Entries<BinMovement>())
        {
            if (entry.State != EntityState.Added || !IsPart(entry.Entity.EntityType))
                continue;
            if (entry.Entity.FromLocationId is int from)
                touched.Add(from);
            if (entry.Entity.ToLocationId is int to)
                touched.Add(to);
        }

        foreach (var entry in context.ChangeTracker.Entries<BinContent>())
        {
            if (!IsPart(entry.Entity.EntityType))
                continue;
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.Quantity != 0m:
                case EntityState.Deleted:
                    touched.Add(entry.Entity.LocationId);
                    break;
                case EntityState.Modified:
                    var quantity = entry.Property(e => e.Quantity);
                    var location = entry.Property(e => e.LocationId);
                    var removed = entry.Property(e => e.RemovedAt);
                    var moved = (quantity.IsModified && quantity.OriginalValue != quantity.CurrentValue)
                        || (location.IsModified && location.OriginalValue != location.CurrentValue)
                        || (removed.IsModified && removed.OriginalValue != removed.CurrentValue);
                    if (!moved)
                        break;
                    touched.Add(entry.Entity.LocationId);
                    if (location.IsModified)
                        touched.Add(location.OriginalValue);
                    break;
            }
        }

        return touched;
    }

    private static bool IsWithin(int locationId, int scopeId, IReadOnlyDictionary<int, int?> parents)
    {
        int? current = locationId;
        var guard = 0;
        while (current is int id && guard++ < 64)
        {
            if (id == scopeId)
                return true;
            current = parents.GetValueOrDefault(id);
        }
        return false;
    }

    private static bool IsPart(string entityType) => string.Equals(entityType, "part", StringComparison.OrdinalIgnoreCase);

    private static PhysicalInventoryFrozenException Refusal(string inventoryNumber) => new(
        inventoryNumber,
        $"Stock here is frozen for physical inventory {inventoryNumber}. Moves, receipts, issues and adjustments in its " +
        "locations wait until the count is posted or cancelled; receive into a location outside the count instead.");
}

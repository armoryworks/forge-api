using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_CheckBomMaterials(
    AppDbContext db,
    IClock clock,
    ILogger<OnSalesOrderConfirmed_CheckBomMaterials> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null)
        {
            logger.LogWarning("SalesOrder {Id} not found for BOM material check", notification.SalesOrderId);
            return;
        }

        var linesWithParts = so.Lines.Where(l => l.PartId.HasValue).ToList();
        if (linesWithParts.Count == 0)
        {
            logger.LogInformation("No parts on SO {OrderNumber} lines — skipping BOM material check", so.OrderNumber);
            return;
        }

        var partIds = linesWithParts.Select(l => l.PartId!.Value).Distinct().ToList();

        // Load all Buy BOM entries for these parts, grouped by parent part
        var buyBomEntries = await db.BOMEntries
            .Include(b => b.ChildPart)
            .Where(b => partIds.Contains(b.ParentPartId) && b.SourceType == BOMSourceType.Buy)
            .AsNoTracking()
            .ToListAsync(ct);

        if (buyBomEntries.Count == 0)
        {
            logger.LogInformation("No Buy BOM entries for SO {OrderNumber} parts — no material check needed", so.OrderNumber);
            return;
        }

        // Build quantity requirements: child part -> total qty needed
        var materialPartIds = buyBomEntries.Select(b => b.ChildPartId).Distinct().ToList();

        // Build a lookup of SO line qty per parent part
        var qtyByParentPart = linesWithParts
            .GroupBy(l => l.PartId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        // Calculate total required qty per child (material) part
        var requiredQtyByMaterial = new Dictionary<int, decimal>();
        foreach (var bom in buyBomEntries)
        {
            if (!qtyByParentPart.TryGetValue(bom.ParentPartId, out var parentQty))
                continue;

            var needed = bom.Quantity * parentQty;
            if (requiredQtyByMaterial.ContainsKey(bom.ChildPartId))
                requiredQtyByMaterial[bom.ChildPartId] += needed;
            else
                requiredQtyByMaterial[bom.ChildPartId] = needed;
        }

        // Get current inventory for these material parts
        var inventoryByPart = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && materialPartIds.Contains(bc.EntityId)
                && bc.Status == BinContentStatus.Stored
                && bc.RemovedAt == null)
            .GroupBy(bc => bc.EntityId)
            .Select(g => new { PartId = g.Key, TotalQty = g.Sum(bc => bc.Quantity - bc.ReservedQuantity) })
            .ToListAsync(ct);

        var inventoryLookup = inventoryByPart.ToDictionary(x => x.PartId, x => x.TotalQty);

        // Build a vendor lookup from BOM entries (prefer first vendor found per child part)
        var vendorByMaterial = buyBomEntries
            .Where(b => b.VendorId.HasValue)
            .GroupBy(b => b.ChildPartId)
            .ToDictionary(g => g.Key, g => g.First().VendorId!.Value);

        // Calculate delivery date for PO suggestions
        var deliveryDate = so.RequestedDeliveryDate ?? clock.UtcNow.AddDays(30);

        // Check for existing pending suggestions for these parts to avoid duplicates
        var existingSuggestionPartIds = await db.AutoPoSuggestions
            .Where(s => materialPartIds.Contains(s.PartId) && s.Status == AutoPoSuggestionStatus.Pending)
            .Select(s => s.PartId)
            .Distinct()
            .ToListAsync(ct);

        var existingSuggestionSet = existingSuggestionPartIds.ToHashSet();

        // Find purchasing/manager users for shortfall notifications
        var purchasingUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager" || x.Name == "OfficeManager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        var suggestionsCreated = 0;
        var notificationsCreated = 0;

        foreach (var (materialPartId, requiredQty) in requiredQtyByMaterial)
        {
            inventoryLookup.TryGetValue(materialPartId, out var availableQty);

            if (availableQty >= requiredQty)
                continue; // Sufficient inventory

            var shortfall = requiredQty - availableQty;

            if (vendorByMaterial.TryGetValue(materialPartId, out var vendorId) && !existingSuggestionSet.Contains(materialPartId))
            {
                db.AutoPoSuggestions.Add(new AutoPoSuggestion
                {
                    PartId = materialPartId,
                    VendorId = vendorId,
                    // Phase 3 / WU-23 (F8-broad): AutoPoSuggestion.SuggestedQty is
                    // now decimal; preserve fractional precision for UoM-aware demand.
                    SuggestedQty = Math.Ceiling(shortfall),
                    NeededByDate = deliveryDate.AddDays(-7), // Need materials before delivery
                    SourceSalesOrderIds = so.Id.ToString(),
                    Status = AutoPoSuggestionStatus.Pending,
                });
                suggestionsCreated++;
            }
            else
            {
                // No vendor on BOM or already has pending suggestion — create a notification
                var materialPart = buyBomEntries.FirstOrDefault(b => b.ChildPartId == materialPartId)?.ChildPart;
                var partDesc = materialPart?.PartNumber ?? $"Part #{materialPartId}";

                foreach (var userId in purchasingUserIds)
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = userId,
                        Type = "material_shortfall",
                        Severity = "warning",
                        Source = "sales_orders",
                        Title = "Material Shortfall Detected",
                        Message = $"Insufficient inventory for {partDesc}: need {requiredQty}, available {availableQty} (shortfall {shortfall}). SO-{so.OrderNumber}.",
                        EntityType = "SalesOrder",
                        EntityId = so.Id,
                        SenderId = notification.UserId,
                    });
                }
                notificationsCreated++;
            }
        }

        if (suggestionsCreated > 0 || notificationsCreated > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "BOM material check for SO {OrderNumber}: {Suggestions} auto-PO suggestion(s), {Notifications} shortfall notification(s)",
            so.OrderNumber, suggestionsCreated, notificationsCreated);
    }
}

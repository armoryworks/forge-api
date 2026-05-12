using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanContextQuery(string PartIdentifier) : IRequest<ScanContextResponseModel>;

public class GetScanContextHandler(AppDbContext db)
    : IRequestHandler<GetScanContextQuery, ScanContextResponseModel>
{
    public async Task<ScanContextResponseModel> Handle(
        GetScanContextQuery request, CancellationToken cancellationToken)
    {
        var identifier = request.PartIdentifier.Trim();

        // Look up part by PartNumber, then fall back to any vendor's catalog
        // SKU (vendor_part_number) — OEM/distributor identity moved off Part
        // onto VendorPart, so external scans hit that intersection table.
        var part = await db.Parts
            .AsNoTracking()
            .Where(p => p.PartNumber == identifier)
            .Select(p => new { p.Id, p.PartNumber, p.Description })
            .FirstOrDefaultAsync(cancellationToken);

        if (part == null)
        {
            var vendorMatch = await db.VendorParts
                .AsNoTracking()
                .Where(vp => vp.VendorPartNumber == identifier || vp.VendorMpn == identifier)
                .Select(vp => new { vp.PartId })
                .FirstOrDefaultAsync(cancellationToken);

            if (vendorMatch != null)
            {
                part = await db.Parts
                    .AsNoTracking()
                    .Where(p => p.Id == vendorMatch.PartId)
                    .Select(p => new { p.Id, p.PartNumber, p.Description })
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        // Also check barcode registry if part not found by part number
        if (part == null)
        {
            var barcode = await db.Barcodes
                .AsNoTracking()
                .Where(b => b.Value == identifier && b.EntityType == BarcodeEntityType.Part && b.PartId.HasValue)
                .Select(b => new { b.PartId })
                .FirstOrDefaultAsync(cancellationToken);

            if (barcode != null)
            {
                part = await db.Parts
                    .AsNoTracking()
                    .Where(p => p.Id == barcode.PartId)
                    .Select(p => new { p.Id, p.PartNumber, p.Description })
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (part == null)
            throw new KeyNotFoundException($"Part not found for identifier '{identifier}'");

        // Get current stock across all locations
        var binContents = await db.BinContents
            .AsNoTracking()
            .Include(bc => bc.Location)
            .Where(bc => bc.EntityType == "part" && bc.EntityId == part.Id && bc.Quantity > 0 && bc.RemovedAt == null)
            .ToListAsync(cancellationToken);

        var totalStock = binContents.Sum(bc => bc.Quantity);
        var primaryLocation = binContents.OrderByDescending(bc => bc.Quantity).FirstOrDefault();

        var actions = new List<ScanAvailableAction>();

        // Move — always available if stock exists
        actions.Add(new ScanAvailableAction(
            "Move",
            totalStock > 0,
            totalStock > 0 ? null : "No stock available to move",
            null));

        // Count — always available
        actions.Add(new ScanAvailableAction("Count", true, null, null));

        // Receive — available if there are open PO lines for this part
        var openPoLines = await db.PurchaseOrderLines
            .AsNoTracking()
            .Include(pol => pol.PurchaseOrder)
            .Where(pol => pol.PartId == part.Id
                && pol.ReceivedQuantity < pol.OrderedQuantity
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Closed)
            .Select(pol => new
            {
                pol.Id,
                pol.PurchaseOrder.PONumber,
                pol.OrderedQuantity,
                pol.ReceivedQuantity,
                Remaining = pol.OrderedQuantity - pol.ReceivedQuantity,
            })
            .ToListAsync(cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Receive",
            openPoLines.Count > 0,
            openPoLines.Count > 0 ? null : "No open purchase order lines",
            openPoLines.Count > 0 ? openPoLines : null));

        // Ship — available if there are open shipment lines for this part
        var openShipmentLines = await db.ShipmentLines
            .AsNoTracking()
            .Include(sl => sl.Shipment)
            .Where(sl => sl.PartId == part.Id
                && sl.Shipment.Status != ShipmentStatus.Delivered
                && sl.Shipment.Status != ShipmentStatus.Cancelled)
            .Select(sl => new
            {
                sl.Id,
                sl.Shipment.TrackingNumber,
                sl.Quantity,
            })
            .ToListAsync(cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Ship",
            openShipmentLines.Count > 0 && totalStock > 0,
            openShipmentLines.Count > 0
                ? (totalStock > 0 ? null : "No stock available to ship")
                : "No open shipment lines",
            openShipmentLines.Count > 0 ? openShipmentLines : null));

        // Issue — available if active jobs use this part in BOM
        var activeJobs = await db.BOMEntries
            .AsNoTracking()
            .Where(bom => bom.ChildPartId == part.Id)
            .Join(
                db.Jobs.Where(j => !j.IsArchived && j.CompletedDate == null),
                bom => bom.ParentPartId,
                j => j.PartId,
                (bom, j) => new { j.Id, j.JobNumber, j.Title })
            .Distinct()
            .ToListAsync(cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Issue",
            activeJobs.Count > 0 && totalStock > 0,
            activeJobs.Count > 0
                ? (totalStock > 0 ? null : "No stock available to issue")
                : "No active jobs require this part",
            activeJobs.Count > 0 ? activeJobs : null));

        // Inspect — available if QC templates exist for this part
        var hasQcTemplate = await db.QcChecklistTemplates
            .AsNoTracking()
            .AnyAsync(t => t.PartId == part.Id, cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Inspect",
            hasQcTemplate,
            hasQcTemplate ? null : "No QC template configured for this part",
            null));

        // Return — always available if stock exists
        actions.Add(new ScanAvailableAction(
            "Return",
            totalStock > 0,
            totalStock > 0 ? null : "No stock available to return",
            null));

        return new ScanContextResponseModel(
            part.Id,
            part.PartNumber,
            part.Description,
            totalStock,
            primaryLocation?.Location.Name,
            primaryLocation?.LocationId,
            actions);
    }
}

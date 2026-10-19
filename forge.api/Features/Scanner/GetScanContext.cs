using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanContextQuery(string PartIdentifier) : IRequest<ScanContextResponseModel>;

public class GetScanContextHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<GetScanContextQuery, ScanContextResponseModel>
{
    private static readonly string[] ShipRoles = ["Admin", "Manager", "OfficeManager"];

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

        // Lot labels resolve to the lot's part — the barcode value IS the lot
        // number, so match either the registry or lot_records directly.
        if (part == null)
        {
            var lotPartId = await db.LotRecords
                .AsNoTracking()
                .Where(l => l.LotNumber == identifier)
                .Select(l => (int?)l.PartId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.Barcodes
                    .AsNoTracking()
                    .Where(b => b.Value == identifier && b.EntityType == BarcodeEntityType.Lot && b.LotRecordId.HasValue)
                    .Select(b => (int?)b.LotRecord!.PartId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (lotPartId.HasValue)
            {
                part = await db.Parts
                    .AsNoTracking()
                    .Where(p => p.Id == lotPartId.Value)
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

        var openPoLines = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PartId == part.Id
                && pol.ReceivedQuantity < pol.OrderedQuantity
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && pol.PurchaseOrder.Status != PurchaseOrderStatus.Closed)
            .OrderBy(pol => pol.PurchaseOrder.PONumber)
            .Select(pol => new ScanReceiveContextLine(
                pol.Id,
                pol.PurchaseOrder.PONumber,
                pol.OrderedQuantity,
                pol.ReceivedQuantity,
                pol.OrderedQuantity - pol.ReceivedQuantity))
            .ToListAsync(cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Receive",
            openPoLines.Count > 0,
            openPoLines.Count > 0 ? null : "No open purchase order lines",
            openPoLines.Count > 0 ? new ScanReceiveContext(openPoLines) : null));

        var openShipmentLines = await db.ShipmentLines
            .AsNoTracking()
            .Where(sl => sl.PartId == part.Id
                && (sl.Shipment.Status == ShipmentStatus.Pending || sl.Shipment.Status == ShipmentStatus.Packed))
            .OrderBy(sl => sl.Shipment.ShipmentNumber)
            .Select(sl => new ScanShipContextLine(
                sl.Id,
                sl.ShipmentId,
                sl.Shipment.ShipmentNumber,
                sl.Shipment.SalesOrder.OrderNumber,
                sl.Quantity))
            .ToListAsync(cancellationToken);

        var user = httpContext.HttpContext?.User;
        var mayShip = ShipRoles.Any(role => user?.IsInRole(role) == true);
        string? shipDisabledReason = openShipmentLines.Count == 0 ? "No open shipment lines"
            : totalStock <= 0 ? "No stock available to ship"
            : !mayShip ? "Your role cannot ship"
            : null;

        actions.Add(new ScanAvailableAction(
            "Ship",
            shipDisabledReason is null,
            shipDisabledReason,
            openShipmentLines.Count > 0 ? new ScanShipContext(openShipmentLines) : null));

        var components = await JobMaterialRules.ForIssuedPartAsync(db, part.Id, cancellationToken);
        var parentPartIds = components.Select(c => c.ParentPartId).Distinct().ToList();
        var activeJobsForParents = parentPartIds.Count == 0
            ? []
            : await db.Jobs
                .AsNoTracking()
                .Where(j => !j.IsArchived && j.CompletedDate == null
                    && j.PartId != null && parentPartIds.Contains(j.PartId.Value))
                .Select(j => new { j.Id, j.JobNumber, j.Title, PartId = j.PartId!.Value })
                .ToListAsync(cancellationToken);
        var bomUses = components
            .Join(activeJobsForParents, c => c.ParentPartId, j => j.PartId,
                (c, j) => new { j.Id, j.JobNumber, j.Title, j.PartId, c.PerUnit })
            .ToList();

        var jobIds = bomUses.Select(u => u.Id).Distinct().ToList();

        var buildQuantities = await db.Set<JobPart>()
            .AsNoTracking()
            .Where(jp => jobIds.Contains(jp.JobId))
            .Join(db.Jobs, jp => jp.JobId, j => j.Id, (jp, j) => new { jp.JobId, jp.PartId, jp.Quantity, JobPartId = j.PartId })
            .Where(x => x.PartId == x.JobPartId)
            .GroupBy(x => x.JobId)
            .Select(g => new { JobId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.JobId, x => x.Quantity, cancellationToken);

        var materialIssued = await db.MaterialIssues
            .AsNoTracking()
            .Where(mi => mi.PartId == part.Id && jobIds.Contains(mi.JobId) && mi.IssueType != MaterialIssueType.Scrap)
            .GroupBy(mi => mi.JobId)
            .Select(g => new
            {
                JobId = g.Key,
                Net = g.Sum(mi => mi.IssueType == MaterialIssueType.Return ? -mi.Quantity : mi.Quantity),
            })
            .ToDictionaryAsync(x => x.JobId, x => x.Net, cancellationToken);

        var scanLogs = await db.ScanActionLogs
            .AsNoTracking()
            .Where(log => log.ActionType == ScanActionType.Issue
                && log.PartId == part.Id
                && log.RelatedEntityType == "Job"
                && log.RelatedEntityId != null
                && jobIds.Contains(log.RelatedEntityId.Value)
                && !log.IsReversed)
            .Select(log => new { log.Id, JobId = log.RelatedEntityId!.Value, log.Quantity })
            .ToListAsync(cancellationToken);
        var scanNotes = scanLogs.Select(log => ScanIssueNote.For(log.Id)).ToList();
        var recordedScanNotes = (await db.MaterialIssues
            .AsNoTracking()
            .Where(mi => mi.Notes != null && scanNotes.Contains(mi.Notes))
            .Select(mi => mi.Notes!)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var scanIssued = scanLogs
            .Where(log => !recordedScanNotes.Contains(ScanIssueNote.For(log.Id)))
            .GroupBy(log => log.JobId)
            .ToDictionary(g => g.Key, g => g.Sum(log => log.Quantity));

        var activeJobs = bomUses
            .GroupBy(u => u.Id)
            .Select(g =>
            {
                var first = g.First();
                var buildQuantity = buildQuantities.TryGetValue(first.Id, out var built) && built > 0 ? built : 1m;
                var required = g.Sum(u => u.PerUnit) * buildQuantity;
                var issued = materialIssued.GetValueOrDefault(first.Id) + scanIssued.GetValueOrDefault(first.Id);
                return new ScanIssueContextJob(
                    first.Id, first.JobNumber, first.Title, required, issued, Math.Max(required - issued, 0));
            })
            .OrderBy(j => j.JobNumber)
            .ToList();

        actions.Add(new ScanAvailableAction(
            "Issue",
            activeJobs.Count > 0 && totalStock > 0,
            activeJobs.Count > 0
                ? (totalStock > 0 ? null : "No stock available to issue")
                : "No active jobs require this part",
            activeJobs.Count > 0 ? new ScanIssueContext(activeJobs) : null));

        var qcTemplateId = await db.QcChecklistTemplates
            .AsNoTracking()
            .Where(t => t.PartId == part.Id && t.IsActive)
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        actions.Add(new ScanAvailableAction(
            "Inspect",
            qcTemplateId.HasValue,
            qcTemplateId.HasValue ? null : "No QC template configured for this part",
            qcTemplateId.HasValue ? new ScanInspectContext(qcTemplateId.Value) : null));

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

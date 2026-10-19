using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

/// <summary>
/// Identifies a scanned value by checking the central Barcode table first,
/// then UserScanIdentifiers (auth-specific RFID/NFC), then legacy EmployeeBarcode field.
/// Returns the entity type and details so the kiosk can route accordingly.
/// </summary>
public record IdentifyScanQuery(string ScanValue) : IRequest<ScanIdentificationResult>;

public record ScanIdentificationResult(
    string ScanType,
    int? EntityId = null,
    string? EntityNumber = null,
    string? EntityTitle = null,
    string? StageName = null,
    string? StageColor = null);

public class IdentifyScanHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager) : IRequestHandler<IdentifyScanQuery, ScanIdentificationResult>
{
    public async Task<ScanIdentificationResult> Handle(IdentifyScanQuery request, CancellationToken cancellationToken)
    {
        var scanValue = request.ScanValue.Trim();

        // 1. Check central Barcode table (primary lookup)
        var barcode = await db.Barcodes
            .FirstOrDefaultAsync(b => b.Value == scanValue && b.IsActive, cancellationToken);

        if (barcode != null)
        {
            return barcode.EntityType switch
            {
                BarcodeEntityType.User => await ResolveEmployeeFromBarcode(barcode, cancellationToken),
                BarcodeEntityType.Job => await ResolveJobResult(barcode.JobId!.Value, cancellationToken),
                BarcodeEntityType.Part => new ScanIdentificationResult("part", barcode.PartId,
                    await db.Parts.Where(p => p.Id == barcode.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(cancellationToken)),
                BarcodeEntityType.SalesOrder => new ScanIdentificationResult("sales-order", barcode.SalesOrderId,
                    await db.SalesOrders.Where(o => o.Id == barcode.SalesOrderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(cancellationToken)),
                BarcodeEntityType.PurchaseOrder => new ScanIdentificationResult("purchase-order", barcode.PurchaseOrderId,
                    await db.PurchaseOrders.Where(o => o.Id == barcode.PurchaseOrderId).Select(o => o.PONumber).FirstOrDefaultAsync(cancellationToken)),
                BarcodeEntityType.Asset => new ScanIdentificationResult("asset", barcode.AssetId,
                    await db.Assets.Where(a => a.Id == barcode.AssetId && !a.IsCustomerOwned).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken)),
                BarcodeEntityType.StorageLocation => await ResolveLocationResult(barcode.StorageLocationId, cancellationToken),
                BarcodeEntityType.Lot => new ScanIdentificationResult("lot", barcode.LotRecordId,
                    await db.LotRecords.Where(l => l.Id == barcode.LotRecordId).Select(l => l.LotNumber).FirstOrDefaultAsync(cancellationToken)),
                _ => new ScanIdentificationResult("unknown"),
            };
        }

        // 2. Check UserScanIdentifiers (RFID, NFC, barcode, biometric — auth-specific)
        var identifier = await db.UserScanIdentifiers
            .FirstOrDefaultAsync(x => x.IdentifierValue == scanValue && x.IsActive, cancellationToken);

        if (identifier != null)
        {
            var user = await userManager.FindByIdAsync(identifier.UserId.ToString());
            return new ScanIdentificationResult("employee", user?.Id, null, user != null ? $"{user.LastName}, {user.FirstName}" : null);
        }

        // 3. Fallback: ApplicationUser.EmployeeBarcode (legacy)
        var employeeByBarcode = await userManager.Users
            .FirstOrDefaultAsync(u => u.EmployeeBarcode == scanValue && u.IsActive, cancellationToken);

        if (employeeByBarcode != null)
            return new ScanIdentificationResult("employee", employeeByBarcode.Id, null, $"{employeeByBarcode.LastName}, {employeeByBarcode.FirstName}");

        // 4. Unknown scan
        return new ScanIdentificationResult("unknown");
    }

    private async Task<ScanIdentificationResult> ResolveEmployeeFromBarcode(Core.Entities.Barcode barcode, CancellationToken cancellationToken)
    {
        if (barcode.UserId.HasValue)
        {
            var user = await userManager.FindByIdAsync(barcode.UserId.Value.ToString());
            if (user != null)
                return new ScanIdentificationResult("employee", user.Id, null, $"{user.LastName}, {user.FirstName}");
        }
        return new ScanIdentificationResult("employee");
    }

    private async Task<ScanIdentificationResult> ResolveLocationResult(int? locationId, CancellationToken cancellationToken)
    {
        if (locationId is not int id)
            return new ScanIdentificationResult("storage-location");

        var names = new List<string>();
        var visited = new HashSet<int>();
        int? currentId = id;
        while (currentId is int current && visited.Add(current))
        {
            var location = await db.StorageLocations
                .Where(l => l.Id == current)
                .Select(l => new { l.Name, l.ParentId })
                .FirstOrDefaultAsync(cancellationToken);
            if (location is null)
                break;
            names.Insert(0, location.Name);
            currentId = location.ParentId;
        }

        return new ScanIdentificationResult("storage-location", id, null, names.Count > 0 ? string.Join(" / ", names) : null);
    }

    private async Task<ScanIdentificationResult> ResolveJobResult(int jobId, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => j.Id == jobId && !j.IsArchived && j.CompletedDate == null)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                StageName = j.CurrentStage != null ? j.CurrentStage.Name : null,
                StageColor = j.CurrentStage != null ? j.CurrentStage.Color : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job != null)
            return new ScanIdentificationResult("job", job.Id, job.JobNumber, job.Title, job.StageName, job.StageColor);

        // Job exists but is archived/completed — still report as job
        return new ScanIdentificationResult("job", jobId);
    }
}

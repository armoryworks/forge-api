using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Barcodes;

/// <summary>
/// Manually add an alternate barcode value to an entity — a manufacturer UPC, a vendor SKU, or a legacy
/// label — on top of its auto-generated system code. The value must be globally unique so a scan resolves
/// to exactly one entity; it then scans the same as the system code. Removable via
/// <see cref="RemoveManualBarcodeCommand"/> (the system code is not).
/// </summary>
public record AddManualBarcodeCommand(BarcodeEntityType EntityType, int EntityId, string Value)
    : IRequest<BarcodeResponseModel>;

public class AddManualBarcodeHandler(AppDbContext db) : IRequestHandler<AddManualBarcodeCommand, BarcodeResponseModel>
{
    public async Task<BarcodeResponseModel> Handle(AddManualBarcodeCommand request, CancellationToken cancellationToken)
    {
        var value = (request.Value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("A barcode value is required.");

        if (!await EntityExistsAsync(request.EntityType, request.EntityId, cancellationToken))
            throw new KeyNotFoundException($"{request.EntityType} {request.EntityId} not found.");

        // Global uniqueness (matches the ix_barcodes_value unique index) so a scan maps to one entity.
        if (await db.Barcodes.AnyAsync(b => b.Value == value, cancellationToken))
            throw new InvalidOperationException($"Barcode value '{value}' is already in use.");

        var barcode = new Barcode
        {
            Value = value,
            EntityType = request.EntityType,
            IsActive = true,
            IdentityType = BarcodeIdentityType.Internal,
            Source = BarcodeSource.Manual,
        };
        SetEntityFk(barcode, request.EntityType, request.EntityId);

        db.Barcodes.Add(barcode);
        db.LogActivityAt(
            "barcode-manual-added",
            $"Alternate barcode added: {value}",
            (ParentEntityName(request.EntityType), request.EntityId));
        await db.SaveChangesAsync(cancellationToken);

        return new BarcodeResponseModel(
            barcode.Id, barcode.Value, barcode.EntityType.ToString(), barcode.IsActive, barcode.CreatedAt,
            barcode.Source.ToString(), barcode.IdentityType.ToString());
    }

    private Task<bool> EntityExistsAsync(BarcodeEntityType type, int id, CancellationToken ct) => type switch
    {
        BarcodeEntityType.Part => db.Parts.AnyAsync(p => p.Id == id, ct),
        BarcodeEntityType.Job => db.Jobs.AnyAsync(j => j.Id == id, ct),
        BarcodeEntityType.SalesOrder => db.SalesOrders.AnyAsync(s => s.Id == id, ct),
        BarcodeEntityType.PurchaseOrder => db.PurchaseOrders.AnyAsync(p => p.Id == id, ct),
        BarcodeEntityType.Asset => db.Assets.AnyAsync(a => a.Id == id, ct),
        BarcodeEntityType.StorageLocation => db.StorageLocations.AnyAsync(l => l.Id == id, ct),
        BarcodeEntityType.Lot => db.LotRecords.AnyAsync(l => l.Id == id, ct),
        BarcodeEntityType.User => db.Users.AnyAsync(u => u.Id == id, ct),
        _ => Task.FromResult(false),
    };

    private static void SetEntityFk(Barcode b, BarcodeEntityType type, int id)
    {
        switch (type)
        {
            case BarcodeEntityType.User: b.UserId = id; break;
            case BarcodeEntityType.Part: b.PartId = id; break;
            case BarcodeEntityType.Job: b.JobId = id; break;
            case BarcodeEntityType.SalesOrder: b.SalesOrderId = id; break;
            case BarcodeEntityType.PurchaseOrder: b.PurchaseOrderId = id; break;
            case BarcodeEntityType.Asset: b.AssetId = id; break;
            case BarcodeEntityType.StorageLocation: b.StorageLocationId = id; break;
            case BarcodeEntityType.Lot: b.LotRecordId = id; break;
        }
    }

    private static string ParentEntityName(BarcodeEntityType type) => type switch
    {
        BarcodeEntityType.User => "ApplicationUser",
        BarcodeEntityType.Part => "Part",
        BarcodeEntityType.Job => "Job",
        BarcodeEntityType.SalesOrder => "SalesOrder",
        BarcodeEntityType.PurchaseOrder => "PurchaseOrder",
        BarcodeEntityType.Asset => "Asset",
        BarcodeEntityType.StorageLocation => "StorageLocation",
        BarcodeEntityType.Lot => "Lot",
        _ => "Barcode",
    };
}

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Barcodes;

/// <summary>
/// Remove a manually-added alternate barcode. Only <see cref="BarcodeSource.Manual"/> rows can be
/// removed — the auto-assigned System code is regenerated (RegenerateBarcode), never deleted, so an
/// entity is never left unscannable.
/// </summary>
public record RemoveManualBarcodeCommand(int BarcodeId) : IRequest;

public class RemoveManualBarcodeHandler(AppDbContext db) : IRequestHandler<RemoveManualBarcodeCommand>
{
    public async Task Handle(RemoveManualBarcodeCommand request, CancellationToken cancellationToken)
    {
        var barcode = await db.Barcodes.FirstOrDefaultAsync(b => b.Id == request.BarcodeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Barcode {request.BarcodeId} not found.");

        if (barcode.Source != BarcodeSource.Manual)
            throw new InvalidOperationException(
                "Only manually-added barcodes can be removed. The auto-assigned code is regenerated, not deleted.");

        var parent = ParentRef(barcode);
        // Hard delete (not soft): the value must be freed from the global unique index so it can be
        // re-registered later; the GetEntityBarcodes list filters on DeletedAt anyway.
        db.Barcodes.Remove(barcode);
        if (parent is { } p)
            db.LogActivityAt("barcode-manual-removed", $"Alternate barcode removed: {barcode.Value}", p);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static (string EntityType, int EntityId)? ParentRef(Barcode b) =>
        b.PartId is int part ? ("Part", part)
        : b.JobId is int job ? ("Job", job)
        : b.SalesOrderId is int so ? ("SalesOrder", so)
        : b.PurchaseOrderId is int po ? ("PurchaseOrder", po)
        : b.AssetId is int asset ? ("Asset", asset)
        : b.StorageLocationId is int loc ? ("StorageLocation", loc)
        : b.LotRecordId is int lot ? ("Lot", lot)
        : b.UserId is int user ? ("ApplicationUser", user)
        : null;
}

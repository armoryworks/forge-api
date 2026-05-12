using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Barcodes;

public record RegenerateBarcodeCommand(BarcodeEntityType EntityType, int EntityId, string NaturalIdentifier)
    : IRequest<BarcodeResponseModel>;

public class RegenerateBarcodeHandler(AppDbContext db, IBarcodeService barcodeService)
    : IRequestHandler<RegenerateBarcodeCommand, BarcodeResponseModel>
{
    public async Task<BarcodeResponseModel> Handle(RegenerateBarcodeCommand request, CancellationToken cancellationToken)
    {
        // Deactivate existing barcodes for this entity
        var baseQuery = db.Barcodes
            .Where(b => b.EntityType == request.EntityType && b.DeletedAt == null);

        var filtered = request.EntityType switch
        {
            BarcodeEntityType.User => baseQuery.Where(b => b.UserId == request.EntityId),
            BarcodeEntityType.Part => baseQuery.Where(b => b.PartId == request.EntityId),
            BarcodeEntityType.Job => baseQuery.Where(b => b.JobId == request.EntityId),
            BarcodeEntityType.SalesOrder => baseQuery.Where(b => b.SalesOrderId == request.EntityId),
            BarcodeEntityType.PurchaseOrder => baseQuery.Where(b => b.PurchaseOrderId == request.EntityId),
            BarcodeEntityType.Asset => baseQuery.Where(b => b.AssetId == request.EntityId),
            BarcodeEntityType.StorageLocation => baseQuery.Where(b => b.StorageLocationId == request.EntityId),
            _ => baseQuery.Where(b => false),
        };

        var existing = await filtered.ToListAsync(cancellationToken);

        foreach (var barcode in existing)
        {
            barcode.IsActive = false;
            barcode.DeletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        var newBarcode = await barcodeService.CreateBarcodeAsync(
            request.EntityType, request.EntityId, request.NaturalIdentifier, cancellationToken);

        return new BarcodeResponseModel(
            newBarcode.Id, newBarcode.Value, newBarcode.EntityType.ToString(),
            newBarcode.IsActive, newBarcode.CreatedAt);
    }
}

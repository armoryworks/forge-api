using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Barcodes;

public record GetEntityBarcodesQuery(BarcodeEntityType EntityType, int EntityId) : IRequest<List<BarcodeResponseModel>>;

public class GetEntityBarcodesHandler(AppDbContext db)
    : IRequestHandler<GetEntityBarcodesQuery, List<BarcodeResponseModel>>
{
    public async Task<List<BarcodeResponseModel>> Handle(GetEntityBarcodesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Barcodes
            .Where(b => b.EntityType == request.EntityType && b.DeletedAt == null);

        query = request.EntityType switch
        {
            BarcodeEntityType.User => query.Where(b => b.UserId == request.EntityId),
            BarcodeEntityType.Part => query.Where(b => b.PartId == request.EntityId),
            BarcodeEntityType.Job => query.Where(b => b.JobId == request.EntityId),
            BarcodeEntityType.SalesOrder => query.Where(b => b.SalesOrderId == request.EntityId),
            BarcodeEntityType.PurchaseOrder => query.Where(b => b.PurchaseOrderId == request.EntityId),
            BarcodeEntityType.Asset => query.Where(b => b.AssetId == request.EntityId),
            BarcodeEntityType.StorageLocation => query.Where(b => b.StorageLocationId == request.EntityId),
            _ => query,
        };

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BarcodeResponseModel(
                b.Id, b.Value, b.EntityType.ToString(), b.IsActive, b.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

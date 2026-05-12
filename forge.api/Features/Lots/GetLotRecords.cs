using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

public record GetLotRecordsQuery(int? PartId, int? JobId, string? Search) : IRequest<List<LotRecordResponseModel>>;

public class GetLotRecordsHandler(AppDbContext db)
    : IRequestHandler<GetLotRecordsQuery, List<LotRecordResponseModel>>
{
    public async Task<List<LotRecordResponseModel>> Handle(
        GetLotRecordsQuery request, CancellationToken cancellationToken)
    {
        var query = db.LotRecords
            .AsNoTracking()
            .Include(l => l.Part)
            .Include(l => l.Job)
            .AsQueryable();

        if (request.PartId.HasValue)
            query = query.Where(l => l.PartId == request.PartId.Value);

        if (request.JobId.HasValue)
            query = query.Where(l => l.JobId == request.JobId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(l =>
                l.LotNumber.Contains(search) ||
                l.SupplierLotNumber != null && l.SupplierLotNumber.Contains(search) ||
                l.Part.PartNumber.Contains(search));
        }

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new LotRecordResponseModel(
                l.Id,
                l.LotNumber,
                l.PartId,
                l.Part.PartNumber,
                l.Part.Description,
                l.JobId,
                l.Job != null ? l.Job.JobNumber : null,
                l.ProductionRunId,
                l.PurchaseOrderLineId,
                l.Quantity,
                l.ExpirationDate,
                l.SupplierLotNumber,
                l.Notes,
                l.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Serials;

public record GetPartSerialsQuery(int PartId, SerialNumberStatus? Status) : IRequest<List<SerialNumberResponseModel>>;

public class GetPartSerialsHandler(AppDbContext db) : IRequestHandler<GetPartSerialsQuery, List<SerialNumberResponseModel>>
{
    public async Task<List<SerialNumberResponseModel>> Handle(GetPartSerialsQuery request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var query = db.SerialNumbers.AsNoTracking()
            .Where(s => s.PartId == request.PartId);

        if (request.Status.HasValue)
            query = query.Where(s => s.Status == request.Status.Value);

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SerialNumberResponseModel(
                s.Id,
                s.PartId,
                s.Part.PartNumber,
                s.SerialValue,
                s.Status,
                s.JobId,
                s.Job != null ? s.Job.JobNumber : null,
                s.LotRecordId,
                s.LotRecordId != null ? db.LotRecords.Where(l => l.Id == s.LotRecordId).Select(l => l.LotNumber).FirstOrDefault() : null,
                s.CurrentLocationId,
                s.CurrentLocation != null ? s.CurrentLocation.Name : null,
                s.ShipmentLineId,
                s.CustomerId,
                s.CustomerId != null ? db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.CompanyName).FirstOrDefault() : null,
                s.ParentSerialId,
                s.ParentSerial != null ? s.ParentSerial.SerialValue : null,
                s.ManufacturedAt,
                s.ShippedAt,
                s.ScrappedAt,
                s.Notes,
                s.CreatedAt,
                s.ChildSerials.Count))
            .ToListAsync(cancellationToken);
    }
}

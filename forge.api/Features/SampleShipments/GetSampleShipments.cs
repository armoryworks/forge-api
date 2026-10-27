using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SampleShipments;

public record GetSampleShipmentsQuery(int? LeadId, int? CustomerId = null) : IRequest<List<SampleShipmentResponseModel>>;

public class GetSampleShipmentsHandler(AppDbContext db) : IRequestHandler<GetSampleShipmentsQuery, List<SampleShipmentResponseModel>>
{
    public async Task<List<SampleShipmentResponseModel>> Handle(GetSampleShipmentsQuery request, CancellationToken ct)
    {
        var q = db.SampleShipments.AsNoTracking().AsQueryable();
        if (request.LeadId.HasValue) q = q.Where(s => s.LeadId == request.LeadId.Value);
        if (request.CustomerId.HasValue) q = q.Where(s => s.CustomerId == request.CustomerId.Value);
        var rows = await q.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return rows.Select(SampleShipmentProjection.ToModel).ToList();
    }
}

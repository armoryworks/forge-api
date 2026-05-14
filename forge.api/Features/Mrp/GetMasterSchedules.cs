using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMasterSchedulesQuery(MasterScheduleStatus? Status = null) : IRequest<List<MasterScheduleResponseModel>>;

public class GetMasterSchedulesHandler(AppDbContext db)
    : IRequestHandler<GetMasterSchedulesQuery, List<MasterScheduleResponseModel>>
{
    public async Task<List<MasterScheduleResponseModel>> Handle(GetMasterSchedulesQuery request, CancellationToken cancellationToken)
    {
        var query = db.MasterSchedules.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(s => s.Status == request.Status.Value);

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new MasterScheduleResponseModel(
                s.Id,
                s.Name,
                s.Description,
                s.Status,
                s.PeriodStart,
                s.PeriodEnd,
                s.CreatedByUserId,
                s.CreatedAt,
                s.Lines.Count
            ))
            .ToListAsync(cancellationToken);
    }
}

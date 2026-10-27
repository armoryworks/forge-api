using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record GetShiftsQuery() : IRequest<List<ShiftResponseModel>>;

public class GetShiftsHandler(AppDbContext db) : IRequestHandler<GetShiftsQuery, List<ShiftResponseModel>>
{
    public async Task<List<ShiftResponseModel>> Handle(GetShiftsQuery request, CancellationToken cancellationToken)
    {
        var shifts = await db.Shifts
            .AsNoTracking()
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        var workCenters = await ShiftWorkCenters.LoadAsync(db, shifts.Select(s => s.Id).ToList(), cancellationToken);

        return shifts
            .Select(s => ShiftWorkCenters.ToResponse(s, workCenters.GetValueOrDefault(s.Id)))
            .ToList();
    }
}

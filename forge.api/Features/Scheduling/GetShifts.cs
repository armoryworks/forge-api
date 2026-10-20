using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
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

        return shifts
            .Select(s => new ShiftResponseModel(
                s.Id, s.Name, s.StartTime, s.EndTime,
                s.BreakMinutes,
                s.NetHours > 0 ? s.NetHours : ShiftHours.Net(s.StartTime, s.EndTime, s.BreakMinutes),
                s.IsActive))
            .ToList();
    }
}

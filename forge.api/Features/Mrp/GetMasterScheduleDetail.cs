using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMasterScheduleDetailQuery(int Id) : IRequest<MasterScheduleDetailResponseModel>;

public class GetMasterScheduleDetailHandler(AppDbContext db)
    : IRequestHandler<GetMasterScheduleDetailQuery, MasterScheduleDetailResponseModel>
{
    public async Task<MasterScheduleDetailResponseModel> Handle(GetMasterScheduleDetailQuery request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .AsNoTracking()
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.Id} not found.");

        return new MasterScheduleDetailResponseModel(
            schedule.Id,
            schedule.Name,
            schedule.Description,
            schedule.Status,
            schedule.PeriodStart,
            schedule.PeriodEnd,
            schedule.CreatedByUserId,
            schedule.CreatedAt,
            schedule.Lines.Select(l => new MasterScheduleLineResponseModel(
                l.Id,
                l.MasterScheduleId,
                l.PartId,
                l.Part?.PartNumber ?? "",
                l.Part?.Description,
                l.Quantity,
                l.DueDate,
                l.Notes
            )).ToList()
        );
    }
}

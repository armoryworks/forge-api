using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Mrp;

public record CancelMasterScheduleCommand(int Id) : IRequest<MasterScheduleResponseModel>;

public class CancelMasterScheduleHandler(AppDbContext db)
    : IRequestHandler<CancelMasterScheduleCommand, MasterScheduleResponseModel>
{
    public async Task<MasterScheduleResponseModel> Handle(CancelMasterScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.Id} not found.");

        if (schedule.Status is not (MasterScheduleStatus.Draft or MasterScheduleStatus.Active))
            throw new InvalidOperationException("Only a draft or active schedule can be cancelled.");

        schedule.Status = MasterScheduleStatus.Cancelled;
        db.LogActivityAt("master-schedule-cancelled", $"Cancelled master schedule {schedule.Name}", ("MasterSchedule", schedule.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new MasterScheduleResponseModel(
            schedule.Id,
            schedule.Name,
            schedule.Description,
            schedule.Status,
            schedule.PeriodStart,
            schedule.PeriodEnd,
            schedule.CreatedByUserId,
            schedule.CreatedAt,
            schedule.Lines.Count
        );
    }
}

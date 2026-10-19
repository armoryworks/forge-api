using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Mrp;

public record CompleteMasterScheduleCommand(int Id) : IRequest<MasterScheduleResponseModel>;

public class CompleteMasterScheduleHandler(AppDbContext db)
    : IRequestHandler<CompleteMasterScheduleCommand, MasterScheduleResponseModel>
{
    public async Task<MasterScheduleResponseModel> Handle(CompleteMasterScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.Id} not found.");

        if (schedule.Status != MasterScheduleStatus.Active)
            throw new InvalidOperationException("Only an active schedule can be completed.");

        schedule.Status = MasterScheduleStatus.Completed;
        db.LogActivityAt("master-schedule-completed", $"Completed master schedule {schedule.Name}", ("MasterSchedule", schedule.Id));
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

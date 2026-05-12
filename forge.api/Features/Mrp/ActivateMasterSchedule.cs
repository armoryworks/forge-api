using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record ActivateMasterScheduleCommand(int Id) : IRequest<MasterScheduleResponseModel>;

public class ActivateMasterScheduleHandler(AppDbContext db)
    : IRequestHandler<ActivateMasterScheduleCommand, MasterScheduleResponseModel>
{
    public async Task<MasterScheduleResponseModel> Handle(ActivateMasterScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.Id} not found.");

        if (schedule.Status != MasterScheduleStatus.Draft)
            throw new InvalidOperationException("Only draft schedules can be activated.");

        if (schedule.Lines.Count == 0)
            throw new InvalidOperationException("Cannot activate a schedule with no lines.");

        schedule.Status = MasterScheduleStatus.Active;
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

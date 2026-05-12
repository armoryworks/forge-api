using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public sealed record DeleteMaintenanceScheduleCommand(int ScheduleId) : IRequest;

public sealed class DeleteMaintenanceScheduleHandler(AppDbContext db)
    : IRequestHandler<DeleteMaintenanceScheduleCommand>
{
    public async Task Handle(DeleteMaintenanceScheduleCommand request, CancellationToken ct)
    {
        var schedule = await db.MaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct)
            ?? throw new KeyNotFoundException($"Maintenance schedule {request.ScheduleId} not found");

        schedule.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

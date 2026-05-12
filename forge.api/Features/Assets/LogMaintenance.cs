using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record LogMaintenanceCommand(int ScheduleId, LogMaintenanceRequestModel Data)
    : IRequest<MaintenanceLogResponseModel>;

public class LogMaintenanceHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<LogMaintenanceCommand, MaintenanceLogResponseModel>
{
    public async Task<MaintenanceLogResponseModel> Handle(
        LogMaintenanceCommand request, CancellationToken ct)
    {
        var schedule = await db.MaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct)
            ?? throw new KeyNotFoundException($"Maintenance schedule {request.ScheduleId} not found");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var now = DateTimeOffset.UtcNow;
        var data = request.Data;

        var log = new MaintenanceLog
        {
            MaintenanceScheduleId = schedule.Id,
            PerformedById = userId,
            PerformedAt = now,
            HoursAtService = data.HoursAtService,
            Notes = data.Notes?.Trim(),
            Cost = data.Cost,
        };

        db.MaintenanceLogs.Add(log);

        schedule.LastPerformedAt = now;
        schedule.NextDueAt = now.AddDays(schedule.IntervalDays);

        await db.SaveChangesAsync(ct);

        var performedByName = $"{user.FirstName} {user.LastName}".Trim();

        return new MaintenanceLogResponseModel(
            log.Id,
            log.MaintenanceScheduleId,
            performedByName,
            log.PerformedAt,
            log.HoursAtService,
            log.Notes,
            log.Cost);
    }
}

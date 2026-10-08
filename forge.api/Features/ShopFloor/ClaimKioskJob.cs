using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record ClaimKioskJobCommand(int JobId, int UserId) : IRequest;

public class ClaimKioskJobHandler(
    AppDbContext db,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    ICapabilitySnapshotProvider capabilities,
    IClock clock) : IRequestHandler<ClaimKioskJobCommand>
{
    public async Task Handle(ClaimKioskJobCommand request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM jobs WHERE id = {request.JobId} FOR UPDATE", ct);

        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.AssigneeId.HasValue)
            throw new InvalidOperationException(AlreadyAssignedMessage(job.JobNumber));

        if (!KioskWork.IsOpenAtShopFloor(job))
            throw new InvalidOperationException($"{job.JobNumber} is not open at a shop-floor status.");

        var worker = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.IsActive, Name = u.LastName + ", " + u.FirstName })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        if (!worker.IsActive)
            throw new InvalidOperationException("An inactive user cannot claim work.");

        await AssigneeComplianceCheck.EnsureCanBeAssigned(db, capabilities, request.UserId, ct);

        job.AssigneeId = request.UserId;
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            UserId = request.UserId,
            Action = ActivityAction.Assigned,
            FieldName = "Assignee",
            NewValue = worker.Name,
            Description = $"Claimed at the kiosk by {worker.Name}.",
            CreatedAt = clock.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var detail = await mediator.Send(new GetJobByIdQuery(job.Id), ct);
        var evt = new BoardJobUpdatedEvent(job.Id, detail);
        await boardHub.Clients.Group($"board:{job.TrackTypeId}").SendAsync("jobUpdated", evt, ct);
        await boardHub.Clients.Group($"job:{job.Id}").SendAsync("jobUpdated", evt, ct);
    }

    private static string AlreadyAssignedMessage(string jobNumber) =>
        $"{jobNumber} is already assigned. Pick another job.";
}

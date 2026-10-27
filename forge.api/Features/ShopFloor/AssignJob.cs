using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record AssignJobCommand(int JobId, int UserId) : IRequest;

public class AssignJobHandler(AppDbContext db, IAssignmentNotifier? assignmentNotifier = null) : IRequestHandler<AssignJobCommand>
{
    public async Task Handle(AssignJobCommand request, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        var reassigned = job.AssigneeId != request.UserId;
        job.AssigneeId = request.UserId;
        await db.SaveChangesAsync(ct);

        if (reassigned && assignmentNotifier is not null)
            await assignmentNotifier.JobAssignedAsync(job.Id, job.JobNumber, job.Title, request.UserId, ct);
    }
}

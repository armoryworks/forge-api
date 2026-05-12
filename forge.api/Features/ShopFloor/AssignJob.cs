using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record AssignJobCommand(int JobId, int UserId) : IRequest;

public class AssignJobHandler(AppDbContext db) : IRequestHandler<AssignJobCommand>
{
    public async Task Handle(AssignJobCommand request, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        job.AssigneeId = request.UserId;
        await db.SaveChangesAsync(ct);
    }
}

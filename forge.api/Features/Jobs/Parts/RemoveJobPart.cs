using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Parts;

public record RemoveJobPartCommand(int JobId, int JobPartId) : IRequest;

public class RemoveJobPartHandler(AppDbContext db) : IRequestHandler<RemoveJobPartCommand>
{
    public async Task Handle(RemoveJobPartCommand request, CancellationToken cancellationToken)
    {
        var jobPart = await db.JobParts
            .FirstOrDefaultAsync(jp => jp.Id == request.JobPartId && jp.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job part with ID {request.JobPartId} not found.");

        db.JobParts.Remove(jobPart);
        await db.SaveChangesAsync(cancellationToken);
    }
}

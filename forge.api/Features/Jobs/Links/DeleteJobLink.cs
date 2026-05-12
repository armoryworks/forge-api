using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Jobs.Links;

public record DeleteJobLinkCommand(int JobId, int LinkId) : IRequest;

public class DeleteJobLinkHandler(
    IJobLinkRepository repo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<DeleteJobLinkCommand>
{
    public async Task Handle(DeleteJobLinkCommand request, CancellationToken cancellationToken)
    {
        var link = await repo.FindAsync(request.LinkId, cancellationToken);
        if (link is null)
            throw new KeyNotFoundException($"Link with ID {request.LinkId} not found.");

        if (link.SourceJobId != request.JobId && link.TargetJobId != request.JobId)
            throw new KeyNotFoundException($"Link with ID {request.LinkId} not found on job {request.JobId}.");

        var otherJobId = link.SourceJobId == request.JobId ? link.TargetJobId : link.SourceJobId;

        await repo.RemoveAsync(link, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Notify both jobs' subscribers
        await boardHub.Clients.Group($"job:{request.JobId}")
            .SendAsync("linkChanged", new { jobId = request.JobId, linkId = request.LinkId, changeType = "deleted" }, cancellationToken);
        await boardHub.Clients.Group($"job:{otherJobId}")
            .SendAsync("linkChanged", new { jobId = otherJobId, changeType = "deleted" }, cancellationToken);
    }
}

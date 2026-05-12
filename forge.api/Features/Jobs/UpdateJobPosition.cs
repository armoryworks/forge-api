using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

public record UpdateJobPositionCommand(int JobId, int Position) : IRequest<Unit>;

public class UpdateJobPositionValidator : AbstractValidator<UpdateJobPositionCommand>
{
    public UpdateJobPositionValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Position).GreaterThanOrEqualTo(0);
    }
}

public class UpdateJobPositionHandler(
    IJobRepository repo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<UpdateJobPositionCommand, Unit>
{
    public async Task<Unit> Handle(UpdateJobPositionCommand request, CancellationToken cancellationToken)
    {
        var job = await repo.FindAsync(request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        job.BoardPosition = request.Position;

        await repo.SaveChangesAsync(cancellationToken);

        // Broadcast position change
        await boardHub.Clients.Group($"board:{job.TrackTypeId}")
            .SendAsync("jobPositionChanged", new BoardJobPositionChangedEvent(
                job.Id, job.CurrentStageId, request.Position), cancellationToken);

        return Unit.Value;
    }
}

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record CompleteJobCommand(int JobId) : IRequest<CompleteJobResponseModel>;

public class CompleteJobHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CompleteJobCommand, CompleteJobResponseModel>
{
    public async Task<CompleteJobResponseModel> Handle(CompleteJobCommand request, CancellationToken ct)
    {
        var status = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        if (status.NextStageId is not { } nextStageId)
            throw new InvalidOperationException("This work order is already at its final status.");

        var nextStage = await db.JobStages.AsNoTracking()
            .Where(s => s.Id == nextStageId)
            .Select(s => new { s.Id, s.Name, s.IsShopFloor })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Stage {nextStageId} not found");

        if (!nextStage.IsShopFloor)
            throw new InvalidOperationException(
                $"The next status, {nextStage.Name}, is an office status. Move it from the board.");

        await mediator.Send(new MoveJobStageCommand(request.JobId, nextStage.Id), ct);

        return new CompleteJobResponseModel(nextStage.Name);
    }
}

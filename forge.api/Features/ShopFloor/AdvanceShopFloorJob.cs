using MediatR;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;

namespace Forge.Api.Features.ShopFloor;

public record AdvanceShopFloorJobCommand(int JobId) : IRequest<JobAdvanceResponseModel>;

public class AdvanceShopFloorJobHandler(IMediator mediator)
    : IRequestHandler<AdvanceShopFloorJobCommand, JobAdvanceResponseModel>
{
    public async Task<JobAdvanceResponseModel> Handle(AdvanceShopFloorJobCommand request, CancellationToken ct)
    {
        var before = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);
        var nextStageId = ShopFloorNextStage.Require(before);

        await mediator.Send(new MoveJobStageCommand(request.JobId, nextStageId), ct);
        var after = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        return new JobAdvanceResponseModel(after, before.StageId, before.StageName, false);
    }
}

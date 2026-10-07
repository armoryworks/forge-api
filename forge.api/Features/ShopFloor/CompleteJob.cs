using MediatR;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Mobile;

namespace Forge.Api.Features.ShopFloor;

public record CompleteJobCommand(int JobId) : IRequest<CompleteJobResponseModel>;

public class CompleteJobHandler(IMediator mediator)
    : IRequestHandler<CompleteJobCommand, CompleteJobResponseModel>
{
    public async Task<CompleteJobResponseModel> Handle(CompleteJobCommand request, CancellationToken ct)
    {
        var status = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);
        var nextStageId = ShopFloorNextStage.Require(status);

        await mediator.Send(new MoveJobStageCommand(request.JobId, nextStageId), ct);

        return new CompleteJobResponseModel(status.NextStageName!);
    }
}

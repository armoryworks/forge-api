using MediatR;

using Forge.Api.Features.Jobs;
using Forge.Api.Services;

namespace Forge.Api.Features.Mobile;

public record AdvanceJobCommand(int JobId, string DeviceKey, string? ScanCode)
    : IRequest<JobAdvanceResponseModel>;

/// <summary>
/// Moves a job to the next column in its track. A duplicate scan (same
/// device, same code, within three seconds) is collapsed to one event: the
/// caller gets the current status back with Collapsed=true and nothing
/// moves twice. The response carries the previous stage so undo can issue
/// the compensating stage move.
/// </summary>
public class AdvanceJobHandler(IMediator mediator, IScanCollapseService collapse)
    : IRequestHandler<AdvanceJobCommand, JobAdvanceResponseModel>
{
    public async Task<JobAdvanceResponseModel> Handle(AdvanceJobCommand request, CancellationToken ct)
    {
        var before = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        if (request.ScanCode is not null
            && collapse.IsDuplicate(request.DeviceKey, request.ScanCode, "advance"))
        {
            return new JobAdvanceResponseModel(
                before, before.PreviousStageId ?? before.StageId, before.PreviousStageName ?? before.StageName, true);
        }

        if (before.NextStageId is null)
            throw new InvalidOperationException("This job is already at the last column of its track.");

        await mediator.Send(new MoveJobStageCommand(request.JobId, before.NextStageId.Value), ct);
        var after = await mediator.Send(new GetJobStatusQuery(request.JobId), ct);

        return new JobAdvanceResponseModel(after, before.StageId, before.StageName, false);
    }
}

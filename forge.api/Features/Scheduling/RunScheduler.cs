using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Scheduling;

public record RunSchedulerCommand(
    ScheduleDirection Direction,
    DateOnly ScheduleFrom,
    DateOnly ScheduleTo,
    int[]? JobIdFilter,
    string PriorityRule,
    int? RunByUserId) : IRequest<ScheduleRunResponseModel>;

public class RunSchedulerHandler(ISchedulingService schedulingService) : IRequestHandler<RunSchedulerCommand, ScheduleRunResponseModel>
{
    public async Task<ScheduleRunResponseModel> Handle(RunSchedulerCommand request, CancellationToken cancellationToken)
    {
        var parameters = new ScheduleParameters(
            request.Direction, request.ScheduleFrom, request.ScheduleTo,
            request.JobIdFilter, request.PriorityRule, request.RunByUserId);

        return await schedulingService.ScheduleAsync(parameters, cancellationToken);
    }
}

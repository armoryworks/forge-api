using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Scheduling;

public record RescheduleOperationCommand(int ScheduledOperationId, DateTimeOffset NewStart) : IRequest;

public class RescheduleOperationHandler(ISchedulingService schedulingService) : IRequestHandler<RescheduleOperationCommand>
{
    public async Task Handle(RescheduleOperationCommand request, CancellationToken cancellationToken)
    {
        await schedulingService.RescheduleOperationAsync(request.ScheduledOperationId, request.NewStart, cancellationToken);
    }
}

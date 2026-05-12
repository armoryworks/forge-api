using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Jobs;

public record RecalculateJobCostsCommand(int JobId) : IRequest;

public class RecalculateJobCostsHandler(IJobCostService costService)
    : IRequestHandler<RecalculateJobCostsCommand>
{
    public Task Handle(RecalculateJobCostsCommand request, CancellationToken cancellationToken)
    {
        return costService.RecalculateTimeEntryCostsAsync(request.JobId, cancellationToken);
    }
}

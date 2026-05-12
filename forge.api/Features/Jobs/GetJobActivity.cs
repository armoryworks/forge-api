using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

public record GetJobActivityQuery(int JobId) : IRequest<List<ActivityResponseModel>>;

public class GetJobActivityHandler(IActivityLogRepository repo) : IRequestHandler<GetJobActivityQuery, List<ActivityResponseModel>>
{
    public async Task<List<ActivityResponseModel>> Handle(GetJobActivityQuery request, CancellationToken cancellationToken)
    {
        var jobExists = await repo.JobExistsAsync(request.JobId, cancellationToken);
        if (!jobExists)
            throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        return await repo.GetByJobIdAsync(request.JobId, cancellationToken);
    }
}

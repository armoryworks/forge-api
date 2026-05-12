using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

public record GetChildJobsQuery(int ParentJobId) : IRequest<List<ChildJobResponseModel>>;

public class GetChildJobsHandler(IJobRepository repo) : IRequestHandler<GetChildJobsQuery, List<ChildJobResponseModel>>
{
    public async Task<List<ChildJobResponseModel>> Handle(GetChildJobsQuery request, CancellationToken cancellationToken)
    {
        _ = await repo.FindAsync(request.ParentJobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.ParentJobId} not found");

        return await repo.GetChildJobsAsync(request.ParentJobId, cancellationToken);
    }
}

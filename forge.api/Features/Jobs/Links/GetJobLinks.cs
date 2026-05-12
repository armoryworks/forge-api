using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Links;

public record GetJobLinksQuery(int JobId) : IRequest<List<JobLinkResponseModel>>;

public class GetJobLinksHandler(IJobLinkRepository repo) : IRequestHandler<GetJobLinksQuery, List<JobLinkResponseModel>>
{
    public Task<List<JobLinkResponseModel>> Handle(GetJobLinksQuery request, CancellationToken cancellationToken)
        => repo.GetByJobIdAsync(request.JobId, cancellationToken);
}

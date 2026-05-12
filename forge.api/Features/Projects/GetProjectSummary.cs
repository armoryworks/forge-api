using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Projects;

public record GetProjectSummaryQuery(int Id) : IRequest<ProjectSummaryResponseModel>;

public class GetProjectSummaryHandler(IProjectAccountingService projectService) : IRequestHandler<GetProjectSummaryQuery, ProjectSummaryResponseModel>
{
    public async Task<ProjectSummaryResponseModel> Handle(GetProjectSummaryQuery query, CancellationToken cancellationToken)
    {
        return await projectService.GetProjectSummaryAsync(query.Id, cancellationToken);
    }
}

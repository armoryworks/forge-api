using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Projects;

public record GetEarnedValueMetricsQuery(int Id) : IRequest<EarnedValueMetricsResponseModel>;

public class GetEarnedValueMetricsHandler(IProjectAccountingService projectService) : IRequestHandler<GetEarnedValueMetricsQuery, EarnedValueMetricsResponseModel>
{
    public async Task<EarnedValueMetricsResponseModel> Handle(GetEarnedValueMetricsQuery query, CancellationToken cancellationToken)
    {
        return await projectService.GetEarnedValueMetricsAsync(query.Id, cancellationToken);
    }
}

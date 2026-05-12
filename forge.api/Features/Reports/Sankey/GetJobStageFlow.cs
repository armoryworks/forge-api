using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetJobStageFlowQuery : IRequest<List<SankeyFlowItem>>;

public class GetJobStageFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetJobStageFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetJobStageFlowQuery request, CancellationToken cancellationToken)
        => repo.GetJobStageFlowAsync(cancellationToken);
}

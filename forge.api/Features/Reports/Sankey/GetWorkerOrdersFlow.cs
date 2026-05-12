using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetWorkerOrdersFlowQuery : IRequest<List<SankeyFlowItem>>;

public class GetWorkerOrdersFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetWorkerOrdersFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetWorkerOrdersFlowQuery request, CancellationToken cancellationToken)
        => repo.GetWorkerOrdersFlowAsync(cancellationToken);
}

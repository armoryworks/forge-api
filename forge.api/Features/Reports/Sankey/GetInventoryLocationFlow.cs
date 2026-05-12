using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetInventoryLocationFlowQuery : IRequest<List<SankeyFlowItem>>;

public class GetInventoryLocationFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetInventoryLocationFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetInventoryLocationFlowQuery request, CancellationToken cancellationToken)
        => repo.GetInventoryLocationFlowAsync(cancellationToken);
}

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetVendorSupplyChainFlowQuery : IRequest<List<SankeyFlowItem>>;

public class GetVendorSupplyChainFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetVendorSupplyChainFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetVendorSupplyChainFlowQuery request, CancellationToken cancellationToken)
        => repo.GetVendorSupplyChainFlowAsync(cancellationToken);
}

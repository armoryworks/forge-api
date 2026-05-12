using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetExpenseFlowQuery(DateTimeOffset? Start, DateTimeOffset? End) : IRequest<List<SankeyFlowItem>>;

public class GetExpenseFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetExpenseFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetExpenseFlowQuery request, CancellationToken cancellationToken)
        => repo.GetExpenseFlowAsync(request.Start, request.End, cancellationToken);
}

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports.Sankey;

public record GetTrainingCompletionFlowQuery : IRequest<List<SankeyFlowItem>>;

public class GetTrainingCompletionFlowHandler(ISankeyReportRepository repo)
    : IRequestHandler<GetTrainingCompletionFlowQuery, List<SankeyFlowItem>>
{
    public Task<List<SankeyFlowItem>> Handle(GetTrainingCompletionFlowQuery request, CancellationToken cancellationToken)
        => repo.GetTrainingCompletionFlowAsync(cancellationToken);
}

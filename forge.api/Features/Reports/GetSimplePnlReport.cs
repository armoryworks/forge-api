using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetSimplePnlReportQuery(
    DateTimeOffset Start,
    DateTimeOffset End) : IRequest<List<SimplePnlReportItem>>;

public class GetSimplePnlReportHandler(IReportRepository repo)
    : IRequestHandler<GetSimplePnlReportQuery, List<SimplePnlReportItem>>
{
    public Task<List<SimplePnlReportItem>> Handle(GetSimplePnlReportQuery request, CancellationToken cancellationToken)
        => repo.GetSimplePnlAsync(request.Start, request.End, cancellationToken);
}

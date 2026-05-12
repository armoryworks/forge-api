using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetArAgingReportQuery : IRequest<List<ArAgingReportItem>>;

public class GetArAgingReportHandler(IReportRepository repo)
    : IRequestHandler<GetArAgingReportQuery, List<ArAgingReportItem>>
{
    public Task<List<ArAgingReportItem>> Handle(GetArAgingReportQuery request, CancellationToken cancellationToken)
        => repo.GetArAgingAsync(cancellationToken);
}

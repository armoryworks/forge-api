using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetAverageLeadTimeReportQuery : IRequest<List<AverageLeadTimeReportItem>>;

public class GetAverageLeadTimeReportHandler(IReportRepository repo)
    : IRequestHandler<GetAverageLeadTimeReportQuery, List<AverageLeadTimeReportItem>>
{
    public async Task<List<AverageLeadTimeReportItem>> Handle(
        GetAverageLeadTimeReportQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAverageLeadTimeAsync(cancellationToken);
    }
}

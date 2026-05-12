using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetOverdueJobsReportQuery : IRequest<List<OverdueJobReportItem>>;

public class GetOverdueJobsReportHandler(IReportRepository repo) : IRequestHandler<GetOverdueJobsReportQuery, List<OverdueJobReportItem>>
{
    public Task<List<OverdueJobReportItem>> Handle(GetOverdueJobsReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetOverdueJobsAsync(cancellationToken);
    }
}

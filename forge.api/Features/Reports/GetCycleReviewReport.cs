using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetCycleReviewReportQuery() : IRequest<List<CycleReviewReportItem>>;

public class GetCycleReviewReportHandler(IReportRepository repo) : IRequestHandler<GetCycleReviewReportQuery, List<CycleReviewReportItem>>
{
    public Task<List<CycleReviewReportItem>> Handle(GetCycleReviewReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetCycleReviewAsync(cancellationToken);
    }
}

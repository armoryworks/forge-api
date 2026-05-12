using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetQualityScrapReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<QualityScrapReportItem>>;

public class GetQualityScrapReportHandler(IReportRepository repo) : IRequestHandler<GetQualityScrapReportQuery, List<QualityScrapReportItem>>
{
    public Task<List<QualityScrapReportItem>> Handle(GetQualityScrapReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetQualityScrapAsync(request.Start, request.End, cancellationToken);
    }
}

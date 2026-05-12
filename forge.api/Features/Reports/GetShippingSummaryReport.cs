using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetShippingSummaryReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<ShippingSummaryReportItem>>;

public class GetShippingSummaryHandler(IReportRepository repo)
    : IRequestHandler<GetShippingSummaryReportQuery, List<ShippingSummaryReportItem>>
{
    public async Task<List<ShippingSummaryReportItem>> Handle(GetShippingSummaryReportQuery request, CancellationToken ct)
    {
        return await repo.GetShippingSummaryAsync(request.Start, request.End, ct);
    }
}

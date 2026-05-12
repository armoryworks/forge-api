using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetRevenueReportQuery(
    DateTimeOffset Start,
    DateTimeOffset End,
    string GroupBy) : IRequest<List<RevenueReportItem>>;

public class GetRevenueReportHandler(IReportRepository repo)
    : IRequestHandler<GetRevenueReportQuery, List<RevenueReportItem>>
{
    public Task<List<RevenueReportItem>> Handle(GetRevenueReportQuery request, CancellationToken cancellationToken)
        => repo.GetRevenueAsync(request.Start, request.End, request.GroupBy, cancellationToken);
}

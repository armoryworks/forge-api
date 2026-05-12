using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetExpenseSummaryReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<ExpenseSummaryReportItem>>;

public class GetExpenseSummaryReportHandler(IReportRepository repo) : IRequestHandler<GetExpenseSummaryReportQuery, List<ExpenseSummaryReportItem>>
{
    public Task<List<ExpenseSummaryReportItem>> Handle(GetExpenseSummaryReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetExpenseSummaryAsync(request.Start, request.End, cancellationToken);
    }
}

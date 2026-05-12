using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetEmployeeProductivityReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<EmployeeProductivityReportItem>>;

public class GetEmployeeProductivityReportHandler(IReportRepository repo) : IRequestHandler<GetEmployeeProductivityReportQuery, List<EmployeeProductivityReportItem>>
{
    public Task<List<EmployeeProductivityReportItem>> Handle(GetEmployeeProductivityReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetEmployeeProductivityAsync(request.Start, request.End, cancellationToken);
    }
}

using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetMaintenanceReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<MaintenanceReportItem>>;

public class GetMaintenanceReportHandler(IReportRepository repo) : IRequestHandler<GetMaintenanceReportQuery, List<MaintenanceReportItem>>
{
    public Task<List<MaintenanceReportItem>> Handle(GetMaintenanceReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetMaintenanceAsync(request.Start, request.End, cancellationToken);
    }
}

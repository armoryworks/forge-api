using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetInventoryLevelsReportQuery() : IRequest<List<InventoryLevelReportItem>>;

public class GetInventoryLevelsReportHandler(IReportRepository repo) : IRequestHandler<GetInventoryLevelsReportQuery, List<InventoryLevelReportItem>>
{
    public Task<List<InventoryLevelReportItem>> Handle(GetInventoryLevelsReportQuery request, CancellationToken cancellationToken)
    {
        return repo.GetInventoryLevelsAsync(cancellationToken);
    }
}

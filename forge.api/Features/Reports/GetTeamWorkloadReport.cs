using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetTeamWorkloadReportQuery : IRequest<List<TeamWorkloadReportItem>>;

public class GetTeamWorkloadReportHandler(IReportRepository repo)
    : IRequestHandler<GetTeamWorkloadReportQuery, List<TeamWorkloadReportItem>>
{
    public async Task<List<TeamWorkloadReportItem>> Handle(
        GetTeamWorkloadReportQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetTeamWorkloadAsync(cancellationToken);
    }
}

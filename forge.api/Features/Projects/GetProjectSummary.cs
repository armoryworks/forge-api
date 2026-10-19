using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record GetProjectSummaryQuery(int Id) : IRequest<ProjectSummaryResponseModel>;

public class GetProjectSummaryHandler(AppDbContext db, IClock clock) : IRequestHandler<GetProjectSummaryQuery, ProjectSummaryResponseModel>
{
    public async Task<ProjectSummaryResponseModel> Handle(GetProjectSummaryQuery query, CancellationToken cancellationToken)
    {
        var rollup = await ProjectRollup.LoadAsync(db, clock, query.Id, track: false, cancellationToken);
        return rollup.Summary();
    }
}

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record GetEarnedValueMetricsQuery(int Id) : IRequest<EarnedValueMetricsResponseModel>;

public class GetEarnedValueMetricsHandler(AppDbContext db, IClock clock) : IRequestHandler<GetEarnedValueMetricsQuery, EarnedValueMetricsResponseModel>
{
    public async Task<EarnedValueMetricsResponseModel> Handle(GetEarnedValueMetricsQuery query, CancellationToken cancellationToken)
    {
        var rollup = await ProjectRollup.LoadAsync(db, clock, query.Id, track: false, cancellationToken);
        return rollup.EarnedValue();
    }
}

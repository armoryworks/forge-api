using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetOeeTrendQuery(int WorkCenterId, DateOnly From, DateOnly To, OeeTrendGranularity Granularity)
    : IRequest<IReadOnlyList<OeeTrendPointModel>>;

public class GetOeeTrendHandler(IOeeService oeeService)
    : IRequestHandler<GetOeeTrendQuery, IReadOnlyList<OeeTrendPointModel>>
{
    public async Task<IReadOnlyList<OeeTrendPointModel>> Handle(GetOeeTrendQuery request, CancellationToken ct)
    {
        return await oeeService.GetOeeTrendAsync(request.WorkCenterId, request.From, request.To, request.Granularity, ct);
    }
}

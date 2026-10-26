using MediatR;

namespace Forge.Api.Features.Accounting;

/// <summary>Standard costing — the overhead pool's actual, applied and pending variance as of a date.</summary>
public record GetOverheadPoolStatusQuery(DateOnly AsOf) : IRequest<OverheadPoolStatusModel>;

public class GetOverheadPoolStatusHandler(IOverheadPoolService pool)
    : IRequestHandler<GetOverheadPoolStatusQuery, OverheadPoolStatusModel>
{
    public Task<OverheadPoolStatusModel> Handle(GetOverheadPoolStatusQuery request, CancellationToken cancellationToken)
        => pool.GetStatusAsync(request.AsOf, cancellationToken);
}

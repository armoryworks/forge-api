using MediatR;

using Forge.Api.Capabilities;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>One opening stock load with its lines.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record GetStockLoadQuery(int Id) : IRequest<StockLoadModel>;

public class GetStockLoadHandler(IStockLoadService stockLoads) : IRequestHandler<GetStockLoadQuery, StockLoadModel>
{
    public Task<StockLoadModel> Handle(GetStockLoadQuery request, CancellationToken ct)
        => stockLoads.GetModelAsync(request.Id, ct);
}

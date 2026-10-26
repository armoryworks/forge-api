using MediatR;

using Forge.Api.Capabilities;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>The opening-stock screen's starting point: the book, the opening journal's date and its inventory debits.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record GetStockLoadContextQuery(int? BookId = null) : IRequest<StockLoadContextModel>;

public class GetStockLoadContextHandler(IStockLoadService stockLoads)
    : IRequestHandler<GetStockLoadContextQuery, StockLoadContextModel>
{
    public Task<StockLoadContextModel> Handle(GetStockLoadContextQuery request, CancellationToken ct)
        => stockLoads.GetContextAsync(request.BookId, ct);
}

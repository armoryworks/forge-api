using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>All acceptance records for a Sales Order (newest first) — drives the acceptance panel + history.</summary>
public record GetSalesOrderAcceptancesQuery(int SalesOrderId) : IRequest<List<SalesOrderAcceptanceResponseModel>>;

public class GetSalesOrderAcceptancesHandler(AppDbContext db)
    : IRequestHandler<GetSalesOrderAcceptancesQuery, List<SalesOrderAcceptanceResponseModel>>
{
    public Task<List<SalesOrderAcceptanceResponseModel>> Handle(GetSalesOrderAcceptancesQuery request, CancellationToken cancellationToken)
        => AcceptanceQuery.ForSalesOrderAsync(db, request.SalesOrderId, cancellationToken);
}

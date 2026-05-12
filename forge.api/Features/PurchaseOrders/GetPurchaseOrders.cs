using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PurchaseOrders;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged purchase-order list query.
///
/// Replaces the previous (vendorId, jobId, status) signature with the bound
/// PurchaseOrderListQuery model. The controller continues to accept the
/// legacy query-param names so existing callers work unchanged.
/// </summary>
public record GetPurchaseOrdersQuery(PurchaseOrderListQuery Query) : IRequest<PagedResponse<PurchaseOrderListItemModel>>;

public class GetPurchaseOrdersHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<GetPurchaseOrdersQuery, PagedResponse<PurchaseOrderListItemModel>>
{
    public Task<PagedResponse<PurchaseOrderListItemModel>> Handle(
        GetPurchaseOrdersQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

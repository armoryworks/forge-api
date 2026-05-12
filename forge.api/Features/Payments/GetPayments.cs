using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Payments;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged payment-list query.
///
/// Replaces the previous (customerId) signature with the bound
/// PaymentListQuery model. The controller continues to accept the legacy
/// query-param names so existing callers work unchanged.
/// </summary>
public record GetPaymentsQuery(PaymentListQuery Query) : IRequest<PagedResponse<PaymentListItemModel>>;

public class GetPaymentsHandler(IPaymentRepository repo)
    : IRequestHandler<GetPaymentsQuery, PagedResponse<PaymentListItemModel>>
{
    public Task<PagedResponse<PaymentListItemModel>> Handle(
        GetPaymentsQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

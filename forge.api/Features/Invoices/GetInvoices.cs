using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged invoice-list query.
///
/// Replaces the previous (customerId, status) signature with the bound
/// InvoiceListQuery model. The controller continues to accept the legacy
/// query-param names so existing callers work unchanged.
/// </summary>
public record GetInvoicesQuery(InvoiceListQuery Query) : IRequest<PagedResponse<InvoiceListItemModel>>;

public class GetInvoicesHandler(IInvoiceRepository repo)
    : IRequestHandler<GetInvoicesQuery, PagedResponse<InvoiceListItemModel>>
{
    public Task<PagedResponse<InvoiceListItemModel>> Handle(
        GetInvoicesQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

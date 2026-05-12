using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Customers;

/// <summary>
/// Phase 3 F7-partial / WU-17 — paged customer-list query.
///
/// The legacy two-arg query (search, isActive) collapses into the new bound
/// model so existing callers continue to work via the controller layer's
/// <c>?search=&amp;isActive=</c> aliasing.
/// </summary>
public record GetCustomerListQuery(CustomerListQuery Query) : IRequest<PagedResponse<CustomerListItemModel>>;

public class GetCustomerListHandler(ICustomerRepository repo)
    : IRequestHandler<GetCustomerListQuery, PagedResponse<CustomerListItemModel>>
{
    public Task<PagedResponse<CustomerListItemModel>> Handle(
        GetCustomerListQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

/// <summary>
/// Phase 3 F7-broad / WU-22 — paged vendor-list query.
///
/// Replaces the previous (search, isActive) signature with the bound
/// VendorListQuery model. The controller continues to accept the legacy
/// query-param names so existing callers work unchanged.
/// </summary>
public record GetVendorsQuery(VendorListQuery Query) : IRequest<PagedResponse<VendorListItemModel>>;

public class GetVendorsHandler(IVendorRepository repo)
    : IRequestHandler<GetVendorsQuery, PagedResponse<VendorListItemModel>>
{
    public Task<PagedResponse<VendorListItemModel>> Handle(
        GetVendorsQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

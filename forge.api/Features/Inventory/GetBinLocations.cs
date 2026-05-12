using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Paged + searchable bin-locations query. The UI uses this for both the
/// inventory-receiving picker and the generic
/// <c>&lt;app-entity-picker entityType="inventory/locations/bins"&gt;</c>
/// shared component.
///
/// Default page size 20, capped at 100. Search filters by bin name +
/// barcode + path (case insensitive).
/// </summary>
public record GetBinLocationsQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 20)
    : IRequest<PagedResponse<StorageLocationFlatResponseModel>>;

public class GetBinLocationsHandler(IInventoryRepository repo)
    : IRequestHandler<GetBinLocationsQuery, PagedResponse<StorageLocationFlatResponseModel>>
{
    public Task<PagedResponse<StorageLocationFlatResponseModel>> Handle(
        GetBinLocationsQuery request, CancellationToken cancellationToken)
        => repo.GetBinLocationsPagedAsync(
            request.Search, request.Page, request.PageSize, cancellationToken);
}

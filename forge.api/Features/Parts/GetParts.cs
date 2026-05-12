using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Phase 3 F7-partial / WU-17 — paged part-list query.
///
/// Replaces the previous (status, type, search) signature with the bound
/// PartListQuery model. The controller continues to accept the legacy
/// query-param names so existing callers work unchanged.
/// </summary>
public record GetPartsQuery(PartListQuery Query) : IRequest<PagedResponse<PartListResponseModel>>;

public class GetPartsHandler(IPartRepository repo)
    : IRequestHandler<GetPartsQuery, PagedResponse<PartListResponseModel>>
{
    public Task<PagedResponse<PartListResponseModel>> Handle(
        GetPartsQuery request, CancellationToken cancellationToken)
        => repo.GetPagedAsync(request.Query, cancellationToken);
}

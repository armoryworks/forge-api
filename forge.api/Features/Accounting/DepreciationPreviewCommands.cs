using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>Dry run of a month's depreciation for a book; posts nothing. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record PreviewDepreciationQuery(int BookId, DateOnly PeriodMonth) : IRequest<DepreciationPreviewResult>;

public class PreviewDepreciationHandler(IDepreciationService service)
    : IRequestHandler<PreviewDepreciationQuery, DepreciationPreviewResult>
{
    public Task<DepreciationPreviewResult> Handle(PreviewDepreciationQuery request, CancellationToken ct)
        => service.PreviewDepreciationAsync(request.BookId, request.PeriodMonth, ct);
}

/// <summary>Company-owned operational assets a fixed asset may book. CAP-ACCT-DEPRECIATION gated.</summary>
[RequiresCapability("CAP-ACCT-DEPRECIATION")]
public record ListLinkableAssetsQuery(int BookId) : IRequest<IReadOnlyList<LinkableAssetModel>>;

public class ListLinkableAssetsHandler(IDepreciationService service)
    : IRequestHandler<ListLinkableAssetsQuery, IReadOnlyList<LinkableAssetModel>>
{
    public Task<IReadOnlyList<LinkableAssetModel>> Handle(ListLinkableAssetsQuery request, CancellationToken ct)
        => service.ListLinkableAssetsAsync(request.BookId, ct);
}

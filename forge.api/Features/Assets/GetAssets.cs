using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Assets;

public record GetAssetsQuery(AssetType? Type, AssetStatus? Status, string? Search) : IRequest<List<AssetResponseModel>>;

public class GetAssetsHandler(IAssetRepository repo) : IRequestHandler<GetAssetsQuery, List<AssetResponseModel>>
{
    public Task<List<AssetResponseModel>> Handle(GetAssetsQuery request, CancellationToken cancellationToken)
        => repo.GetAssetsAsync(request.Type, request.Status, request.Search, cancellationToken);
}

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Assets;

// AS-01: single-asset read. Detail surfaces previously fetched the whole list and
// found the row client-side; this is the dedicated GET /api/v1/assets/{id}.
public record GetAssetByIdQuery(int Id) : IRequest<AssetResponseModel>;

public class GetAssetByIdHandler(IAssetRepository repo) : IRequestHandler<GetAssetByIdQuery, AssetResponseModel>
{
    public async Task<AssetResponseModel> Handle(GetAssetByIdQuery request, CancellationToken cancellationToken)
        => await repo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Asset {request.Id} not found");
}

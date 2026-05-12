using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAssetRepository
{
    Task<List<AssetResponseModel>> GetAssetsAsync(AssetType? type, AssetStatus? status, string? search, CancellationToken ct);
    Task<AssetResponseModel?> GetByIdAsync(int id, CancellationToken ct);
    Task<Asset?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(Asset asset, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

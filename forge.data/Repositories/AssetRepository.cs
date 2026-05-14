using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class AssetRepository(AppDbContext db) : IAssetRepository
{
    public async Task<List<AssetResponseModel>> GetAssetsAsync(AssetType? type, AssetStatus? status, string? search, CancellationToken ct)
    {
        var query = db.Assets.AsQueryable();

        if (type.HasValue)
            query = query.Where(a => a.AssetType == type.Value);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Name.ToLower().Contains(term) ||
                (a.Manufacturer != null && a.Manufacturer.ToLower().Contains(term)) ||
                (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(term)));
        }

        var assets = await query
            .Include(a => a.SourceJob)
            .Include(a => a.SourcePart)
            .Include(a => a.WorkCenter)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        return assets.Select(ToResponseModel).ToList();
    }

    public async Task<AssetResponseModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var asset = await db.Assets
            .Include(a => a.SourceJob)
            .Include(a => a.SourcePart)
            .Include(a => a.WorkCenter)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        return asset is null ? null : ToResponseModel(asset);
    }

    public Task<Asset?> FindAsync(int id, CancellationToken ct)
        => db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Asset asset, CancellationToken ct)
    {
        await db.Assets.AddAsync(asset, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static AssetResponseModel ToResponseModel(Asset a) => new(
        a.Id, a.Name, a.AssetType, a.Location, a.Manufacturer, a.Model,
        a.SerialNumber, a.Status, a.PhotoFileId, a.CurrentHours, a.Notes,
        a.IsCustomerOwned, a.CavityCount, a.ToolLifeExpectancy, a.CurrentShotCount,
        a.SourceJobId, a.SourceJob?.JobNumber, a.SourcePartId, a.SourcePart?.PartNumber,
        a.CreatedAt, a.UpdatedAt,
        // Phase 3 F4 — full-record fields surfaced on the GET response.
        AcquisitionCost: a.AcquisitionCost,
        DepreciationMethod: a.DepreciationMethod,
        WorkCenterId: a.WorkCenterId,
        WorkCenterCode: a.WorkCenter?.Code,
        GlAccount: a.GlAccount);
}

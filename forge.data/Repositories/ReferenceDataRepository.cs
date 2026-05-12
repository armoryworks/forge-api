using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ReferenceDataRepository(AppDbContext db) : IReferenceDataRepository
{
    public async Task<List<ReferenceDataGroupResponseModel>> GetAllGroupsAsync(CancellationToken ct)
    {
        var allData = await db.ReferenceData
            .OrderBy(r => r.GroupCode)
            .ThenBy(r => r.SortOrder)
            .ToListAsync(ct);

        return allData
            .GroupBy(r => r.GroupCode)
            .Select(g => new ReferenceDataGroupResponseModel(
                g.Key,
                g.Select(r => new ReferenceDataResponseModel(
                    r.Id, r.Code, r.Label, r.SortOrder, r.IsActive, r.IsSeedData,
                    r.EffectiveFrom, r.EffectiveTo, r.Metadata, r.ParentId))
                .ToList()))
            .ToList();
    }

    public async Task<List<ReferenceDataResponseModel>> GetByGroupAsync(string groupCode, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return await db.ReferenceData
            .Where(r => r.GroupCode == groupCode && r.IsActive)
            .Where(r => r.EffectiveFrom == null || r.EffectiveFrom <= now)
            .Where(r => r.EffectiveTo == null || r.EffectiveTo > now)
            .OrderBy(r => r.SortOrder)
            .Select(r => new ReferenceDataResponseModel(
                r.Id, r.Code, r.Label, r.SortOrder, r.IsActive, r.IsSeedData,
                r.EffectiveFrom, r.EffectiveTo, r.Metadata, r.ParentId))
            .ToListAsync(ct);
    }
}

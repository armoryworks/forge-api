using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetInternalProjectTypesQuery() : IRequest<List<ReferenceDataResponseModel>>;

public class GetInternalProjectTypesHandler(AppDbContext db)
    : IRequestHandler<GetInternalProjectTypesQuery, List<ReferenceDataResponseModel>>
{
    public async Task<List<ReferenceDataResponseModel>> Handle(GetInternalProjectTypesQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return await db.ReferenceData
            .Where(r => r.GroupCode == "internal_project_type" && r.IsActive)
            .Where(r => r.EffectiveFrom == null || r.EffectiveFrom <= now)
            .Where(r => r.EffectiveTo == null || r.EffectiveTo > now)
            .OrderBy(r => r.SortOrder)
            .Select(r => new ReferenceDataResponseModel(r.Id, r.Code, r.Label, r.SortOrder, r.IsActive, r.IsSeedData,
                r.EffectiveFrom, r.EffectiveTo, r.Metadata, r.ParentId))
            .ToListAsync(ct);
    }
}

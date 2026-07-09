using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing;

/// <summary>
/// Read the active costing profile (Tier 2 config surface). Returns the current <see cref="CostingProfile"/>
/// — effective today, else the seeded "default" — with its departmental per-work-center overhead rates
/// parsed into a typed list. When no profile has been configured yet, returns a flat default so the admin
/// panel has a stable shape to bind to.
/// </summary>
public record GetCostingProfileQuery : IRequest<CostingProfileResponseModel>;

public class GetCostingProfileHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetCostingProfileQuery, CostingProfileResponseModel>
{
    public async Task<CostingProfileResponseModel> Handle(GetCostingProfileQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var profile = await db.Set<CostingProfile>().AsNoTracking()
            .Where(p => p.EffectiveFrom <= today && (p.EffectiveTo == null || p.EffectiveTo >= today))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Set<CostingProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == "default", cancellationToken);

        if (profile is null)
            return new CostingProfileResponseModel("flat", []);

        var rates = StandardCostRollupService.ParseDepartmentalRates(profile.DepartmentalRates)
            .Select(kv => new DepartmentalRateModel(kv.Key, kv.Value))
            .OrderBy(r => r.WorkCenterId)
            .ToList();

        return new CostingProfileResponseModel(profile.Mode, rates);
    }
}

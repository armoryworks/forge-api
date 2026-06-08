using System.Security.Claims;
using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Recalculate + freeze a part's standard cost — runs the cost rollup (routing × work-center rates + recursive
/// BOM material), reconciles to the manual override if set, and persists the decomposed result as a current
/// <see cref="CostCalculation"/> snapshot (with <see cref="CostCalculationInputs"/>). The resolver then reads
/// the frozen snapshot instead of recomputing, giving an auditable point-in-time standard. Master-data
/// operation — runnable before CAP-ACCT-FULLGL is enabled (it's how standards get populated for go-live).
/// </summary>
public record RecalculatePartStandardCostCommand(int PartId) : IRequest<RecalculatedStandardCostModel>;

public sealed record RecalculatedStandardCostModel(
    int PartId, decimal Material, decimal Labor, decimal Overhead, decimal Total, int CostCalculationId);

public class RecalculatePartStandardCostHandler(
    AppDbContext db,
    IStandardCostRollupService rollup,
    IClock clock,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<RecalculatePartStandardCostCommand, RecalculatedStandardCostModel>
{
    public async Task<RecalculatedStandardCostModel> Handle(
        RecalculatePartStandardCostCommand request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found.");

        var rolled = await rollup.RollupAsync(part.Id, cancellationToken);
        var elements = StandardCostElements.ReconcileToOverride(rolled, part.ManualCostOverride);

        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var now = clock.UtcNow;
        var profile = await EnsureDefaultProfileAsync(now, cancellationToken);

        // Supersede any prior current snapshot for this part (IsCurrent is true on exactly one row).
        var priors = await db.Set<CostCalculation>()
            .Where(c => c.EntityType == "Part" && c.EntityId == part.Id && c.IsCurrent)
            .ToListAsync(cancellationToken);
        foreach (var p in priors)
            p.IsCurrent = false;

        var calc = new CostCalculation
        {
            EntityType = "Part",
            EntityId = part.Id,
            ProfileId = profile.Id,
            ProfileVersion = 1, // no profile-versioning scheme yet; the rollup uses work-center rates directly
            ResultAmount = elements.Total,
            ResultBreakdown = JsonSerializer.Serialize(new
            {
                material = elements.Material,
                labor = elements.Labor,
                overhead = elements.Overhead,
            }),
            CalculatedAt = now,
            CalculatedBy = userId,
            IsCurrent = true,
            Inputs = new CostCalculationInputs
            {
                DirectMaterialCost = elements.Material,
                DirectLaborCost = elements.Labor,
                OverheadAmount = elements.Overhead,
            },
        };
        db.Add(calc);
        await db.SaveChangesAsync(cancellationToken);

        part.CurrentCostCalculationId = calc.Id;
        await db.SaveChangesAsync(cancellationToken);

        return new RecalculatedStandardCostModel(
            part.Id, elements.Material, elements.Labor, elements.Overhead, elements.Total, calc.Id);
    }

    /// <summary>Find-or-create the default flat costing profile (none is seeded by default).</summary>
    private async Task<CostingProfile> EnsureDefaultProfileAsync(DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.Set<CostingProfile>().FirstOrDefaultAsync(p => p.Code == "default", ct);
        if (profile is null)
        {
            profile = new CostingProfile { Code = "default", Mode = "flat", EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime) };
            db.Add(profile);
            await db.SaveChangesAsync(ct);
        }
        return profile;
    }
}

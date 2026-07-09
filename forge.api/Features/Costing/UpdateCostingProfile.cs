using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing;

/// <summary>
/// Configure the active costing profile (Tier 2). Upserts the seeded "default" <see cref="CostingProfile"/>:
/// sets its <c>Mode</c> (flat | departmental) and, for departmental mode, the per-work-center overhead
/// percentages serialized into <c>DepartmentalRates</c> as <c>[{ work_center_id, rate_pct }]</c>. The cost
/// rollup consults this profile on the next recalculation. Admin-only, gated by CAP-COSTING-TIER2-DEPTRATES.
/// </summary>
public record UpdateCostingProfileCommand(string Mode, List<DepartmentalRateModel> DepartmentalRates) : IRequest;

public class UpdateCostingProfileValidator : AbstractValidator<UpdateCostingProfileCommand>
{
    private static readonly string[] AllowedModes = ["flat", "departmental"];

    public UpdateCostingProfileValidator()
    {
        RuleFor(x => x.Mode)
            .Must(m => AllowedModes.Contains(m, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Mode must be 'flat' or 'departmental'.");

        RuleForEach(x => x.DepartmentalRates).ChildRules(r =>
        {
            r.RuleFor(x => x.WorkCenterId).GreaterThan(0);
            r.RuleFor(x => x.RatePct).InclusiveBetween(0m, 100_000m)
                .WithMessage("Rate percent must be between 0 and 100000.");
        });
    }
}

public class UpdateCostingProfileHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpdateCostingProfileCommand>
{
    public async Task Handle(UpdateCostingProfileCommand request, CancellationToken cancellationToken)
    {
        var profile = await db.Set<CostingProfile>().FirstOrDefaultAsync(p => p.Code == "default", cancellationToken);
        if (profile is null)
        {
            profile = new CostingProfile { Code = "default", EffectiveFrom = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime) };
            db.Add(profile);
        }

        var mode = request.Mode.Equals("departmental", StringComparison.OrdinalIgnoreCase) ? "departmental" : "flat";

        // Persist only meaningful rows (>0); departmental mode with an empty grid is legal (every work
        // center then falls back to its flat burden rate). Store null in flat mode — no stale rate config.
        var rates = request.DepartmentalRates
            .Where(r => r.WorkCenterId > 0)
            .GroupBy(r => r.WorkCenterId)
            .Select(g => new { work_center_id = g.Key, rate_pct = g.Last().RatePct })
            .OrderBy(r => r.work_center_id)
            .ToList();

        profile.Mode = mode;
        profile.DepartmentalRates = mode == "departmental" && rates.Count > 0
            ? JsonSerializer.Serialize(rates, StandardCostRollupService.DeptRateJson)
            : null;

        // Persist first so a freshly-created profile has a real Id for the activity row to reference.
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("costing-profile-updated",
            $"Costing mode set to {mode}" + (mode == "departmental" ? $" ({rates.Count} departmental rate(s))" : string.Empty),
            ("CostingProfile", profile.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}

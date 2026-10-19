using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// The conversion-cost policy shared by the standard rollup and actual time-entry costing — flat (Tier 1,
/// burden rate × hours) or departmental (Tier 2, a per-work-center percentage of direct labor). Standard and
/// actual overhead must use the same policy, or the job close posts a phantom overhead-efficiency variance.
/// </summary>
public sealed record CostingConversionPolicy(bool Departmental, IReadOnlyDictionary<int, decimal> WorkCenterRatePct)
{
    public const string DeptRatesCapability = "CAP-COSTING-TIER2-DEPTRATES";

    /// <summary>snake_case JSON for the <c>[{ work_center_id, rate_pct }]</c> departmental-rate array.</summary>
    internal static readonly JsonSerializerOptions DeptRateJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static CostingConversionPolicy Flat { get; } = new(false, new Dictionary<int, decimal>());

    private sealed record DepartmentalRateRow(int WorkCenterId, decimal RatePct);

    /// <summary>
    /// Overhead for one slice of work. Departmental mode applies the work center's percentage of
    /// <paramref name="labor"/>; work centers with no configured rate, and flat mode, use
    /// <paramref name="burdenPerHour"/> × <paramref name="hours"/>.
    /// </summary>
    public decimal Overhead(int? workCenterId, decimal hours, decimal labor, decimal burdenPerHour) =>
        Departmental && workCenterId is int wc && WorkCenterRatePct.TryGetValue(wc, out var pct)
            ? labor * (pct / 100m)
            : hours * burdenPerHour;

    /// <summary>
    /// Resolve the policy in force on <paramref name="asOf"/>. Departmental (Tier 2) applies only when
    /// <c>CAP-COSTING-TIER2-DEPTRATES</c> is enabled AND the active <see cref="CostingProfile"/> (effective
    /// that day, else the "default" row) is in <c>departmental</c> mode; otherwise flat Tier-1 rates apply.
    /// </summary>
    public static async Task<CostingConversionPolicy> LoadAsync(
        AppDbContext db, ICapabilitySnapshotProvider? capabilities, DateOnly asOf, CancellationToken ct)
    {
        if (capabilities is null || !capabilities.IsEnabled(DeptRatesCapability))
            return Flat;

        var profile = await db.Set<CostingProfile>().AsNoTracking()
            .Where(p => p.EffectiveFrom <= asOf && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct)
            ?? await db.Set<CostingProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == "default", ct);

        if (profile is null || !string.Equals(profile.Mode, "departmental", StringComparison.OrdinalIgnoreCase))
            return Flat;

        return new CostingConversionPolicy(true, ParseDepartmentalRates(profile.DepartmentalRates));
    }

    /// <summary>Parse the <c>[{ work_center_id, rate_pct }]</c> array into a work-center → percent lookup.</summary>
    internal static IReadOnlyDictionary<int, decimal> ParseDepartmentalRates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<int, decimal>();
        try
        {
            var rows = JsonSerializer.Deserialize<List<DepartmentalRateRow>>(json, DeptRateJson);
            var map = new Dictionary<int, decimal>();
            foreach (var r in rows ?? [])
                map[r.WorkCenterId] = r.RatePct; // last-write-wins on duplicate work-center ids
            return map;
        }
        catch (JsonException)
        {
            return new Dictionary<int, decimal>(); // malformed config never breaks the rollup — degrade to flat
        }
    }
}

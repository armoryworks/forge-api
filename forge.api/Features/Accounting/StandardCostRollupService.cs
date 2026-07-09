using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Computes a part's standard unit cost decomposed into material / labor / overhead from the captured master
/// data — the "cost rollup" the variance decomposition needs (the D5 cost-recalc engine, used live by
/// <see cref="StandardCostResolver"/> now and available to persist into <c>CostCalculation</c> later):
/// <list type="bullet">
///   <item><b>Labor</b> = Σ operations <c>(EstimatedMinutes/60) × WorkCenter.LaborCostPerHour</c>.</item>
///   <item><b>Overhead (flat, Tier 1)</b> = Σ operations <c>(EstimatedMinutes/60) × WorkCenter.BurdenRatePerHour</c>
///         (work-center burden rate × hours).</item>
///   <item><b>Overhead (departmental, Tier 2)</b> = Σ operations <c>opLaborCost × ratePct(WorkCenter)/100</c> —
///         a per-work-center percentage of direct labor from the active <see cref="CostingProfile"/> in
///         <c>departmental</c> mode; work centers with no configured rate fall back to the flat burden rate.
///         Only applied when <c>CAP-COSTING-TIER2-DEPTRATES</c> is enabled.</item>
///   <item><b>Material</b> = Σ BOM lines <c>Quantity × child standard cost</c>, recursive for sub-assemblies
///         (a child's standard is its own rollup, or its ManualCostOverride for a purchased leaf).</item>
/// </list>
/// Cycle-guarded against BOM loops. Subcontract operations are excluded (their cost lands via the PO receipt).
/// </summary>
public interface IStandardCostRollupService
{
    Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class StandardCostRollupService(
    AppDbContext db, IClock clock, ICapabilitySnapshotProvider capabilities) : IStandardCostRollupService
{
    private const int MaxDepth = 32; // defensive bound on BOM depth (cycle guard already prevents true loops)
    private const string DeptRatesCapability = "CAP-COSTING-TIER2-DEPTRATES";

    /// <summary>snake_case JSON for the <c>[{ work_center_id, rate_pct }]</c> departmental-rate array.</summary>
    internal static readonly JsonSerializerOptions DeptRateJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Resolved conversion policy for one rollup call — flat (Tier 1) or departmental (Tier 2).</summary>
    private sealed record ConversionPolicy(bool Departmental, IReadOnlyDictionary<int, decimal> WorkCenterRatePct);

    private sealed record DepartmentalRateRow(int WorkCenterId, decimal RatePct);

    public async Task<StandardCostElements> RollupAsync(int partId, CancellationToken ct = default)
    {
        var policy = await LoadConversionPolicyAsync(ct);
        return await RollupAsync(partId, policy, new HashSet<int>(), 0, ct);
    }

    private async Task<StandardCostElements> RollupAsync(int partId, ConversionPolicy policy, HashSet<int> visited, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth || !visited.Add(partId))
            return StandardCostElements.Zero; // cycle / runaway guard

        try
        {
            var (labor, overhead) = await RollupConversionAsync(partId, policy, ct);

            // Material from the live part BOM (BOMLine: parent → child × qty), recursive.
            var bomLines = await db.BOMLines.AsNoTracking()
                .Where(b => b.ParentPartId == partId)
                .Select(b => new { b.ChildPartId, b.Quantity })
                .ToListAsync(ct);

            decimal material = 0m;
            foreach (var line in bomLines)
            {
                var child = await RollupAsync(line.ChildPartId, policy, visited, depth + 1, ct);
                var childUnit = child.Total;
                if (childUnit <= 0m)
                    // Leaf / purchased child with no rollup — fall back to its manual standard.
                    childUnit = await db.Parts.AsNoTracking()
                        .Where(p => p.Id == line.ChildPartId)
                        .Select(p => p.ManualCostOverride ?? 0m)
                        .FirstOrDefaultAsync(ct);
                material += line.Quantity * childUnit;
            }

            return new StandardCostElements(
                decimal.Round(material, 4), decimal.Round(labor, 4), decimal.Round(overhead, 4));
        }
        finally
        {
            visited.Remove(partId);
        }
    }

    /// <summary>Labor + overhead from the part's routing (operations × time × work-center rates).</summary>
    private async Task<(decimal Labor, decimal Overhead)> RollupConversionAsync(int partId, ConversionPolicy policy, CancellationToken ct)
    {
        var ops = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId && !o.IsSubcontract && o.WorkCenterId != null && o.EstimatedMinutes != null)
            .Select(o => new { Minutes = o.EstimatedMinutes!.Value, WorkCenterId = o.WorkCenterId!.Value })
            .ToListAsync(ct);
        if (ops.Count == 0)
            return (0m, 0m);

        var wcIds = ops.Select(o => o.WorkCenterId).Distinct().ToList();
        var rates = await db.WorkCenters.AsNoTracking()
            .Where(w => wcIds.Contains(w.Id))
            .Select(w => new { w.Id, w.LaborCostPerHour, w.BurdenRatePerHour })
            .ToDictionaryAsync(w => w.Id, w => (w.LaborCostPerHour, w.BurdenRatePerHour), ct);

        decimal labor = 0m, overhead = 0m;
        foreach (var op in ops)
        {
            if (op.Minutes <= 0 || !rates.TryGetValue(op.WorkCenterId, out var rate))
                continue;
            var hours = op.Minutes / 60m;
            var opLabor = hours * rate.LaborCostPerHour;
            labor += opLabor;

            // Tier 2 (departmental): a per-work-center percentage of this op's direct labor. Work centers
            // with no configured departmental rate — and Tier-1/flat mode — fall back to burden × hours.
            if (policy.Departmental && policy.WorkCenterRatePct.TryGetValue(op.WorkCenterId, out var pct))
                overhead += opLabor * (pct / 100m);
            else
                overhead += hours * rate.BurdenRatePerHour;
        }
        return (labor, overhead);
    }

    /// <summary>
    /// Resolve the active costing policy once per rollup. Departmental (Tier 2) applies only when
    /// <c>CAP-COSTING-TIER2-DEPTRATES</c> is enabled AND the active <see cref="CostingProfile"/> (effective
    /// today, else the "default" row) is in <c>departmental</c> mode; otherwise flat Tier-1 rates apply.
    /// </summary>
    private async Task<ConversionPolicy> LoadConversionPolicyAsync(CancellationToken ct)
    {
        var flat = new ConversionPolicy(false, new Dictionary<int, decimal>());
        if (!capabilities.IsEnabled(DeptRatesCapability))
            return flat;

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var profile = await db.Set<CostingProfile>().AsNoTracking()
            .Where(p => p.EffectiveFrom <= today && (p.EffectiveTo == null || p.EffectiveTo >= today))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct)
            ?? await db.Set<CostingProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == "default", ct);

        if (profile is null || !string.Equals(profile.Mode, "departmental", StringComparison.OrdinalIgnoreCase))
            return flat;

        return new ConversionPolicy(true, ParseDepartmentalRates(profile.DepartmentalRates));
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

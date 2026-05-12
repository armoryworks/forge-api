using System.Text.Json;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class SpcService(AppDbContext db) : ISpcService
{
    // SPC constants for sample sizes 2-25 (A2, D3, D4, d2, A3, B3, B4, c4)
    private static readonly Dictionary<int, SpcConstants> ConstantsTable = new()
    {
        [2]  = new(1.880m, 0m,     3.267m, 1.128m, 2.659m, 0m,     3.267m, 0.7979m),
        [3]  = new(1.023m, 0m,     2.575m, 1.693m, 1.954m, 0m,     2.568m, 0.8862m),
        [4]  = new(0.729m, 0m,     2.282m, 2.059m, 1.628m, 0m,     2.266m, 0.9213m),
        [5]  = new(0.577m, 0m,     2.115m, 2.326m, 1.427m, 0m,     2.089m, 0.9400m),
        [6]  = new(0.483m, 0m,     2.004m, 2.534m, 1.287m, 0.030m, 1.970m, 0.9515m),
        [7]  = new(0.419m, 0.076m, 1.924m, 2.704m, 1.182m, 0.118m, 1.882m, 0.9594m),
        [8]  = new(0.373m, 0.136m, 1.864m, 2.847m, 1.099m, 0.185m, 1.815m, 0.9650m),
        [9]  = new(0.337m, 0.184m, 1.816m, 2.970m, 1.032m, 0.239m, 1.761m, 0.9693m),
        [10] = new(0.308m, 0.223m, 1.777m, 3.078m, 0.975m, 0.284m, 1.716m, 0.9727m),
        [15] = new(0.223m, 0.348m, 1.652m, 3.472m, 0.789m, 0.428m, 1.572m, 0.9823m),
        [20] = new(0.180m, 0.414m, 1.586m, 3.735m, 0.680m, 0.510m, 1.490m, 0.9869m),
        [25] = new(0.153m, 0.459m, 1.541m, 3.931m, 0.606m, 0.565m, 1.435m, 0.9896m),
    };

    public SpcConstants GetConstants(int sampleSize)
    {
        if (ConstantsTable.TryGetValue(sampleSize, out var constants))
            return constants;

        // Interpolate for sizes not in table (rare, but handle gracefully)
        var keys = ConstantsTable.Keys.OrderBy(k => k).ToList();
        var lower = keys.LastOrDefault(k => k <= sampleSize);
        var upper = keys.FirstOrDefault(k => k >= sampleSize);

        if (lower == 0) lower = keys[0];
        if (upper == 0) upper = keys[^1];
        if (lower == upper) return ConstantsTable[lower];

        // Linear interpolation
        var lc = ConstantsTable[lower];
        var uc = ConstantsTable[upper];
        var t = (decimal)(sampleSize - lower) / (upper - lower);

        return new SpcConstants(
            Lerp(lc.A2, uc.A2, t), Lerp(lc.D3, uc.D3, t), Lerp(lc.D4, uc.D4, t), Lerp(lc.d2, uc.d2, t),
            Lerp(lc.A3, uc.A3, t), Lerp(lc.B3, uc.B3, t), Lerp(lc.B4, uc.B4, t), Lerp(lc.c4, uc.c4, t));
    }

    private static decimal Lerp(decimal a, decimal b, decimal t) => a + (b - a) * t;

    public decimal CalculateCp(decimal usl, decimal lsl, decimal sigma)
        => sigma > 0 ? (usl - lsl) / (6m * sigma) : 0;

    public decimal CalculateCpk(decimal usl, decimal lsl, decimal mean, decimal sigma)
        => sigma > 0 ? Math.Min((usl - mean) / (3m * sigma), (mean - lsl) / (3m * sigma)) : 0;

    public decimal CalculatePp(decimal usl, decimal lsl, decimal overallSigma)
        => overallSigma > 0 ? (usl - lsl) / (6m * overallSigma) : 0;

    public decimal CalculatePpk(decimal usl, decimal lsl, decimal mean, decimal overallSigma)
        => overallSigma > 0 ? Math.Min((usl - mean) / (3m * overallSigma), (mean - lsl) / (3m * overallSigma)) : 0;

    public async Task<SpcControlLimit> CalculateControlLimitsAsync(
        int characteristicId, int? fromSubgroup, int? toSubgroup, CancellationToken ct)
    {
        var characteristic = await db.SpcCharacteristics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == characteristicId, ct)
            ?? throw new KeyNotFoundException($"SPC Characteristic {characteristicId} not found");

        var query = db.SpcMeasurements
            .AsNoTracking()
            .Where(m => m.CharacteristicId == characteristicId);

        if (fromSubgroup.HasValue)
            query = query.Where(m => m.SubgroupNumber >= fromSubgroup.Value);
        if (toSubgroup.HasValue)
            query = query.Where(m => m.SubgroupNumber <= toSubgroup.Value);

        var measurements = await query
            .OrderBy(m => m.SubgroupNumber)
            .ToListAsync(ct);

        if (measurements.Count < 2)
            throw new InvalidOperationException("At least 2 subgroups are required to calculate control limits.");

        var constants = GetConstants(characteristic.SampleSize);

        // X-bar and R calculations
        var xBarValues = measurements.Select(m => m.Mean).ToList();
        var rangeValues = measurements.Select(m => m.Range).ToList();

        var xDoubleBar = xBarValues.Average(); // Grand mean
        var rBar = rangeValues.Average();       // Average range

        // Control limits using A2, D3, D4
        var xBarUcl = xDoubleBar + constants.A2 * rBar;
        var xBarLcl = xDoubleBar - constants.A2 * rBar;
        var rangeUcl = constants.D4 * rBar;
        var rangeLcl = constants.D3 * rBar;

        // Estimated sigma from R-bar/d2
        var sigma = constants.d2 > 0 ? rBar / constants.d2 : 0;

        // Overall sigma (from all individual values)
        var allValues = measurements
            .SelectMany(m => ParseValues(m.ValuesJson))
            .ToList();
        var overallMean = allValues.Count > 0 ? allValues.Average() : xDoubleBar;
        var overallSigma = allValues.Count > 1
            ? (decimal)Math.Sqrt((double)allValues.Sum(v => (v - overallMean) * (v - overallMean)) / (allValues.Count - 1))
            : sigma;

        // S-chart limits (for sample sizes > 10)
        decimal? sUcl = null, sLcl = null, sCenterLine = null;
        if (characteristic.SampleSize >= 10)
        {
            var sValues = measurements.Select(m => m.StdDev).ToList();
            var sBar = sValues.Average();
            sCenterLine = sBar;
            sUcl = constants.B4 * sBar;
            sLcl = constants.B3 * sBar;
        }

        var cp = CalculateCp(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, sigma);
        var cpk = CalculateCpk(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, xDoubleBar, sigma);
        var pp = CalculatePp(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, overallSigma);
        var ppk = CalculatePpk(characteristic.UpperSpecLimit, characteristic.LowerSpecLimit, overallMean, overallSigma);

        // Deactivate existing active limits
        var existingActive = await db.SpcControlLimits
            .Where(l => l.CharacteristicId == characteristicId && l.IsActive)
            .ToListAsync(ct);

        foreach (var existing in existingActive)
            existing.IsActive = false;

        var controlLimit = new SpcControlLimit
        {
            CharacteristicId = characteristicId,
            CalculatedAt = DateTimeOffset.UtcNow,
            SampleCount = measurements.Count,
            FromSubgroup = measurements[0].SubgroupNumber,
            ToSubgroup = measurements[^1].SubgroupNumber,
            XBarUcl = xBarUcl,
            XBarLcl = xBarLcl,
            XBarCenterLine = xDoubleBar,
            RangeUcl = rangeUcl,
            RangeLcl = rangeLcl,
            RangeCenterLine = rBar,
            SUcl = sUcl,
            SLcl = sLcl,
            SCenterLine = sCenterLine,
            Cp = cp,
            Cpk = cpk,
            Pp = pp,
            Ppk = ppk,
            ProcessSigma = sigma,
            IsActive = true,
        };

        db.SpcControlLimits.Add(controlLimit);
        await db.SaveChangesAsync(ct);

        return controlLimit;
    }

    public IReadOnlyList<SpcOocEvent> EvaluateControlRules(
        SpcCharacteristic characteristic,
        SpcControlLimit limits,
        IReadOnlyList<SpcMeasurement> recentSubgroups)
    {
        var events = new List<SpcOocEvent>();

        if (recentSubgroups.Count == 0) return events;

        var xBarValues = recentSubgroups.Select(m => m.Mean).ToList();
        var latest = recentSubgroups[^1];

        // Western Electric Rule 1: One point beyond 3σ (beyond control limits)
        if (latest.Mean > limits.XBarUcl || latest.Mean < limits.XBarLcl)
        {
            events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                "WE1_BeyondLimit",
                $"Point #{latest.SubgroupNumber} (X̄={latest.Mean:F4}) is beyond control limits [{limits.XBarLcl:F4}, {limits.XBarUcl:F4}]",
                SpcOocSeverity.OutOfControl));
        }

        // Check spec limits
        if (latest.Mean > characteristic.UpperSpecLimit || latest.Mean < characteristic.LowerSpecLimit)
        {
            events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                "OutOfSpec",
                $"Point #{latest.SubgroupNumber} (X̄={latest.Mean:F4}) is beyond specification limits [{characteristic.LowerSpecLimit:F4}, {characteristic.UpperSpecLimit:F4}]",
                SpcOocSeverity.OutOfSpec));
        }

        // Western Electric Rule 2: 2 of 3 consecutive points beyond 2σ (same side)
        if (xBarValues.Count >= 3)
        {
            var twoSigmaUp = limits.XBarCenterLine + 2m * (limits.XBarUcl - limits.XBarCenterLine) / 3m;
            var twoSigmaDown = limits.XBarCenterLine - 2m * (limits.XBarCenterLine - limits.XBarLcl) / 3m;

            var last3 = xBarValues.TakeLast(3).ToList();
            var aboveCount = last3.Count(v => v > twoSigmaUp);
            var belowCount = last3.Count(v => v < twoSigmaDown);

            if (aboveCount >= 2)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE2_TwoOfThree",
                    $"2 of 3 consecutive points are beyond 2σ (upper side)",
                    SpcOocSeverity.Warning));
            }
            if (belowCount >= 2)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE2_TwoOfThree",
                    $"2 of 3 consecutive points are beyond 2σ (lower side)",
                    SpcOocSeverity.Warning));
            }
        }

        // Western Electric Rule 3: 4 of 5 consecutive points beyond 1σ (same side)
        if (xBarValues.Count >= 5)
        {
            var oneSigmaUp = limits.XBarCenterLine + (limits.XBarUcl - limits.XBarCenterLine) / 3m;
            var oneSigmaDown = limits.XBarCenterLine - (limits.XBarCenterLine - limits.XBarLcl) / 3m;

            var last5 = xBarValues.TakeLast(5).ToList();
            var aboveCount = last5.Count(v => v > oneSigmaUp);
            var belowCount = last5.Count(v => v < oneSigmaDown);

            if (aboveCount >= 4)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE3_FourOfFive",
                    $"4 of 5 consecutive points are beyond 1σ (upper side)",
                    SpcOocSeverity.Warning));
            }
            if (belowCount >= 4)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE3_FourOfFive",
                    $"4 of 5 consecutive points are beyond 1σ (lower side)",
                    SpcOocSeverity.Warning));
            }
        }

        // Western Electric Rule 4: 8 consecutive points on one side of center line
        if (xBarValues.Count >= 8)
        {
            var last8 = xBarValues.TakeLast(8).ToList();
            var allAbove = last8.All(v => v > limits.XBarCenterLine);
            var allBelow = last8.All(v => v < limits.XBarCenterLine);

            if (allAbove)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE4_EightConsecutive",
                    $"8 consecutive points above center line — process shift detected",
                    SpcOocSeverity.OutOfControl));
            }
            if (allBelow)
            {
                events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                    "WE4_EightConsecutive",
                    $"8 consecutive points below center line — process shift detected",
                    SpcOocSeverity.OutOfControl));
            }
        }

        // Range chart check: range beyond UCL
        if (latest.Range > limits.RangeUcl)
        {
            events.Add(CreateOocEvent(characteristic.Id, latest.Id,
                "Range_BeyondUCL",
                $"Range value {latest.Range:F4} exceeds UCL {limits.RangeUcl:F4} — excessive variation",
                SpcOocSeverity.OutOfControl));
        }

        return events;
    }

    public async Task<SpcChartDataModel> GetXBarRChartDataAsync(
        int characteristicId, int? lastNSubgroups, CancellationToken ct)
    {
        var characteristic = await db.SpcCharacteristics
            .AsNoTracking()
            .Include(c => c.Part)
            .FirstOrDefaultAsync(c => c.Id == characteristicId, ct)
            ?? throw new KeyNotFoundException($"SPC Characteristic {characteristicId} not found");

        var activeLimits = await db.SpcControlLimits
            .AsNoTracking()
            .Where(l => l.CharacteristicId == characteristicId && l.IsActive)
            .FirstOrDefaultAsync(ct);

        var query = db.SpcMeasurements
            .AsNoTracking()
            .Where(m => m.CharacteristicId == characteristicId)
            .OrderBy(m => m.SubgroupNumber);

        List<SpcMeasurement> measurements;
        if (lastNSubgroups.HasValue)
        {
            var total = await query.CountAsync(ct);
            measurements = await query
                .Skip(Math.Max(0, total - lastNSubgroups.Value))
                .ToListAsync(ct);
        }
        else
        {
            measurements = await query.ToListAsync(ct);
        }

        var points = measurements.Select(m => new SpcChartPointModel
        {
            SubgroupNumber = m.SubgroupNumber,
            MeasuredAt = m.MeasuredAt,
            Mean = m.Mean,
            Range = m.Range,
            StdDev = m.StdDev,
            IsOoc = m.IsOutOfControl,
            OocRule = m.OocRuleViolated,
        }).ToList();

        SpcControlLimitModel? limitsModel = null;
        if (activeLimits != null)
        {
            limitsModel = new SpcControlLimitModel
            {
                XBarUcl = activeLimits.XBarUcl,
                XBarLcl = activeLimits.XBarLcl,
                XBarCenterLine = activeLimits.XBarCenterLine,
                RangeUcl = activeLimits.RangeUcl,
                RangeLcl = activeLimits.RangeLcl,
                RangeCenterLine = activeLimits.RangeCenterLine,
                Cp = activeLimits.Cp,
                Cpk = activeLimits.Cpk,
                Pp = activeLimits.Pp,
                Ppk = activeLimits.Ppk,
                ProcessSigma = activeLimits.ProcessSigma,
                SampleCount = activeLimits.SampleCount,
                IsActive = activeLimits.IsActive,
            };
        }

        return new SpcChartDataModel
        {
            CharacteristicId = characteristic.Id,
            CharacteristicName = characteristic.Name,
            Usl = characteristic.UpperSpecLimit,
            Lsl = characteristic.LowerSpecLimit,
            Nominal = characteristic.NominalValue,
            ActiveLimits = limitsModel,
            Points = points,
        };
    }

    private static SpcOocEvent CreateOocEvent(int characteristicId, int measurementId,
        string ruleName, string description, SpcOocSeverity severity) => new()
    {
        CharacteristicId = characteristicId,
        MeasurementId = measurementId,
        DetectedAt = DateTimeOffset.UtcNow,
        RuleName = ruleName,
        Description = description,
        Severity = severity,
        Status = SpcOocStatus.Open,
    };

    private static List<decimal> ParseValues(string valuesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<decimal>>(valuesJson) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

internal static class DecimalExtensions
{
    public static decimal Average(this IList<decimal> values)
    {
        if (values.Count == 0) return 0;
        return values.Sum() / values.Count;
    }
}

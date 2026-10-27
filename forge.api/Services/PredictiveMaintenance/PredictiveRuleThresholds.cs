using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Services.PredictiveMaintenance;

/// <summary>
/// The rule thresholds in force for one asset class: the install values from <see cref="PredictiveMaintenanceSettings"/>
/// with that class's entry in the per-class JSON laid over them, and the shipped defaults for anything unset or
/// unreadable.
/// </summary>
public sealed record PredictiveRuleThresholds(
    decimal UsageThresholdPercent,
    decimal DowntimeTrendFactor,
    decimal CycleDriftRatio,
    int CycleDriftRuns,
    decimal ScrapTrendFactor,
    int SuppressionDays)
{
    public static readonly PredictiveRuleThresholds Defaults = new(
        PredictiveMaintenanceSettings.DefaultUsageThresholdPercent,
        PredictiveMaintenanceSettings.DefaultDowntimeTrendFactor,
        PredictiveMaintenanceSettings.DefaultCycleDriftRatio,
        PredictiveMaintenanceSettings.DefaultCycleDriftRuns,
        PredictiveMaintenanceSettings.DefaultScrapTrendFactor,
        PredictiveMaintenanceSettings.DefaultSuppressionDays);

    public static async Task<Func<AssetType, PredictiveRuleThresholds>> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith(PredictiveMaintenanceSettings.Prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, ct);

        var install = Apply(Defaults, key => rows.TryGetValue(PredictiveMaintenanceSettings.Prefix + key, out var v) ? v : null);
        var perClass = ParseOverrides(rows.GetValueOrDefault(PredictiveMaintenanceSettings.AssetClassOverridesKey));

        return assetType => perClass.TryGetValue(assetType.ToString(), out var values)
            ? Apply(install, key => values.GetValueOrDefault(key))
            : install;
    }

    private static PredictiveRuleThresholds Apply(PredictiveRuleThresholds basis, Func<string, string?> read) => new(
        Decimal(read(PredictiveMaintenanceSettings.UsageThresholdPercentKey)) ?? basis.UsageThresholdPercent,
        Decimal(read(PredictiveMaintenanceSettings.DowntimeTrendFactorKey)) ?? basis.DowntimeTrendFactor,
        Decimal(read(PredictiveMaintenanceSettings.CycleDriftRatioKey)) ?? basis.CycleDriftRatio,
        (int?)Decimal(read(PredictiveMaintenanceSettings.CycleDriftRunsKey)) is int runs and > 0 ? runs : basis.CycleDriftRuns,
        Decimal(read(PredictiveMaintenanceSettings.ScrapTrendFactorKey)) ?? basis.ScrapTrendFactor,
        (int?)Decimal(read(PredictiveMaintenanceSettings.SuppressionDaysKey)) is int days and >= 0 ? days : basis.SuppressionDays);

    private static decimal? Decimal(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;

    private static Dictionary<string, Dictionary<string, string?>> ParseOverrides(string? json)
    {
        var result = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var assetClass in document.RootElement.EnumerateObject())
            {
                if (assetClass.Value.ValueKind != JsonValueKind.Object)
                    continue;
                result[assetClass.Name] = assetClass.Value.EnumerateObject()
                    .ToDictionary(
                        p => p.Name,
                        p => p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String ? p.Value.ToString() : null,
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        return result;
    }
}

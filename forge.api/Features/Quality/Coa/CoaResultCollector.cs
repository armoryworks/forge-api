using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>
/// Collects the results a certificate of analysis reports for a lot. From completed QC inspections stamped
/// with the lot (for the lot's part, or with no part), the latest recorded result of each checklist item;
/// from SPC, one row per characteristic of the lot's part that has measurements for the lot, summarising
/// every reading (mean, count, lowest and highest) against the characteristic's specification limits.
/// A row passes when the inspector passed it, or when no SPC reading is out of specification.
/// </summary>
public static class CoaResultCollector
{
    private static readonly string[] CompletedStatuses = ["Passed", "Failed"];

    public static async Task<List<CoaResult>> CollectAsync(AppDbContext db, LotRecord lot, CancellationToken ct)
    {
        var rows = new List<CoaResult>();
        rows.AddRange(await FromInspectionsAsync(db, lot, ct));
        rows.AddRange(await FromSpcAsync(db, lot, ct));
        for (var i = 0; i < rows.Count; i++)
            rows[i].SortOrder = i + 1;
        return rows;
    }

    private static async Task<List<CoaResult>> FromInspectionsAsync(AppDbContext db, LotRecord lot, CancellationToken ct)
    {
        var results = await db.QcInspectionResults.AsNoTracking()
            .Where(r => r.Inspection.LotNumber == lot.LotNumber
                && (r.Inspection.PartId == null || r.Inspection.PartId == lot.PartId)
                && CompletedStatuses.Contains(r.Inspection.Status)
                && r.Passed != null)
            .Select(r => new
            {
                r.Id,
                r.ChecklistItemId,
                r.Description,
                r.Specification,
                r.MeasuredValue,
                Passed = r.Passed!.Value,
                TemplateName = r.Inspection.Template != null ? r.Inspection.Template.Name : null,
                InspectedAt = r.Inspection.CompletedAt ?? r.Inspection.CreatedAt,
                ItemOrder = r.ChecklistItem != null ? r.ChecklistItem.SortOrder : int.MaxValue,
            })
            .ToListAsync(ct);

        return results
            .GroupBy(r => r.ChecklistItemId?.ToString(CultureInfo.InvariantCulture) ?? r.Description.Trim().ToLowerInvariant())
            .Select(g => g.OrderByDescending(r => r.InspectedAt).ThenByDescending(r => r.Id).First())
            .OrderBy(r => r.ItemOrder).ThenBy(r => r.Id)
            .Select(r =>
            {
                var (min, max, target) = CoaSpecificationParser.Parse(r.Specification);
                return new CoaResult
                {
                    Characteristic = Clip(r.Description, 300),
                    Specification = r.Specification is null ? null : Clip(r.Specification, 500),
                    SpecMin = min,
                    SpecMax = max,
                    SpecTarget = target,
                    Result = r.MeasuredValue is null ? null : Clip(r.MeasuredValue, 200),
                    Method = r.TemplateName is null ? null : Clip(r.TemplateName, 200),
                    Pass = r.Passed,
                    SourceType = CoaResult.SourceInspection,
                    SourceId = r.Id,
                };
            })
            .ToList();
    }

    private static async Task<List<CoaResult>> FromSpcAsync(AppDbContext db, LotRecord lot, CancellationToken ct)
    {
        var measurements = await db.SpcMeasurements.AsNoTracking()
            .Where(m => m.LotNumber == lot.LotNumber && m.Characteristic.PartId == lot.PartId)
            .Select(m => new
            {
                m.CharacteristicId,
                m.Characteristic.Name,
                m.Characteristic.NominalValue,
                m.Characteristic.LowerSpecLimit,
                m.Characteristic.UpperSpecLimit,
                m.Characteristic.UnitOfMeasure,
                m.Characteristic.DecimalPlaces,
                m.ValuesJson,
                m.Mean,
                m.IsOutOfSpec,
            })
            .ToListAsync(ct);

        return measurements
            .GroupBy(m => m.CharacteristicId)
            .OrderBy(g => g.First().Name)
            .Select(g =>
            {
                var c = g.First();
                var values = g.SelectMany(m => Values(m.ValuesJson, m.Mean)).ToList();
                var low = values.Min();
                var high = values.Max();
                var mean = values.Average();
                var places = Math.Clamp(c.DecimalPlaces, 0, 6);
                var pass = !g.Any(m => m.IsOutOfSpec) && low >= c.LowerSpecLimit && high <= c.UpperSpecLimit;
                return new CoaResult
                {
                    Characteristic = Clip(c.Name, 300),
                    Specification = $"{Format(c.LowerSpecLimit, places)} – {Format(c.UpperSpecLimit, places)}",
                    SpecMin = c.LowerSpecLimit,
                    SpecMax = c.UpperSpecLimit,
                    SpecTarget = c.NominalValue,
                    Result = $"{Format(mean, places)} (n={values.Count}, {Format(low, places)} – {Format(high, places)})",
                    Unit = c.UnitOfMeasure is null ? null : Clip(c.UnitOfMeasure, 30),
                    Method = "SPC",
                    Pass = pass,
                    SourceType = CoaResult.SourceSpc,
                    SourceId = c.CharacteristicId,
                };
            })
            .ToList();
    }

    private static IEnumerable<decimal> Values(string valuesJson, decimal mean)
    {
        var parsed = JsonSerializer.Deserialize<List<decimal>>(string.IsNullOrWhiteSpace(valuesJson) ? "[]" : valuesJson);
        return parsed is { Count: > 0 } ? parsed : [mean];
    }

    private static string Format(decimal value, int places) =>
        Math.Round(value, places, MidpointRounding.AwayFromZero).ToString("F" + places, CultureInfo.InvariantCulture);

    private static string Clip(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

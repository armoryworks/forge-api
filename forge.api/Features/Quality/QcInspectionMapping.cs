using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>Shared loaders that map persisted QC inspections, with their result snapshots, to responses.</summary>
internal static class QcInspectionMapping
{
    public static async Task<QcInspectionResponseModel> LoadResponseAsync(
        AppDbContext db, int inspectionId, CancellationToken ct)
    {
        var inspection = await db.QcInspections
            .AsNoTracking()
            .Include(i => i.Results)
            .Include(i => i.Job)
            .Include(i => i.Part)
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
            ?? throw new KeyNotFoundException($"Inspection {inspectionId} not found.");

        return (await ToResponseModelsAsync(db, [inspection], ct))[0];
    }

    public static async Task<List<QcInspectionResponseModel>> ToResponseModelsAsync(
        AppDbContext db, IReadOnlyList<QcInspection> inspections, CancellationToken ct)
    {
        var inspectorNames = await InspectorNamesAsync(db, inspections.Select(i => i.InspectorId), ct);
        var templateNames = await TemplateNamesAsync(db, inspections.Select(i => i.TemplateId), ct);

        return inspections.Select(i => new QcInspectionResponseModel(
            i.Id,
            i.JobId,
            i.Job?.JobNumber,
            i.ProductionRunId,
            i.TemplateId,
            i.TemplateId is int templateId ? templateNames.GetValueOrDefault(templateId) : null,
            i.PartId,
            i.Part?.PartNumber,
            i.InspectorId,
            inspectorNames.GetValueOrDefault(i.InspectorId, string.Empty),
            i.LotNumber,
            i.Status,
            i.Notes,
            i.CompletedAt,
            i.Results.OrderBy(r => r.Id).Select(ToResultModel).ToList(),
            i.CreatedAt))
            .ToList();
    }

    public static QcInspectionResultModel ToResultModel(QcInspectionResult r) => new(
        r.Id,
        r.ChecklistItemId,
        r.Description,
        r.Specification,
        r.IsRequired,
        r.Passed,
        r.MeasuredValue,
        r.Notes);

    public static async Task<Dictionary<int, string>> InspectorNamesAsync(
        AppDbContext db, IEnumerable<int> inspectorIds, CancellationToken ct)
    {
        var ids = inspectorIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);
    }

    public static async Task<Dictionary<int, string>> TemplateNamesAsync(
        AppDbContext db, IEnumerable<int?> templateIds, CancellationToken ct)
    {
        var ids = templateIds.OfType<int>().Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.QcChecklistTemplates
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}

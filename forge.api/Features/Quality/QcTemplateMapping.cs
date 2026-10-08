using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>Shared loader that maps a QC checklist template and its items to its response.</summary>
internal static class QcTemplateMapping
{
    public static async Task<QcTemplateResponseModel> LoadResponseAsync(
        AppDbContext db, int templateId, CancellationToken ct)
    {
        return await db.QcChecklistTemplates
            .AsNoTracking()
            .Where(t => t.Id == templateId)
            .Select(t => new QcTemplateResponseModel(
                t.Id,
                t.Name,
                t.Description,
                t.PartId,
                t.Part != null ? t.Part.PartNumber : null,
                t.IsActive,
                t.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new QcTemplateItemModel(
                    i.Id,
                    i.Description,
                    i.Specification,
                    i.SortOrder,
                    i.IsRequired
                )).ToList()))
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Checklist template {templateId} not found.");
    }
}

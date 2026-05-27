using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

/// <summary>
/// F-14-BE-01 — shared projection so the create/update path handlers return the same
/// shape as GetTrainingPaths. Admin write context, so per-user progress is null.
/// </summary>
internal static class TrainingPathResponseBuilder
{
    public static async Task<TrainingPathResponseModel> BuildAsync(
        AppDbContext db, TrainingPath path, IReadOnlyList<int> moduleIds, CancellationToken ct)
    {
        var modules = moduleIds.Count == 0
            ? new List<TrainingModule>()
            : await db.TrainingModules.Where(m => moduleIds.Contains(m.Id)).ToListAsync(ct);

        var pathModules = moduleIds
            .Select((moduleId, index) =>
            {
                var module = modules.FirstOrDefault(m => m.Id == moduleId);
                return new TrainingPathModuleResponseModel(
                    moduleId,
                    module?.Title ?? string.Empty,
                    module?.ContentType ?? default,
                    module?.EstimatedMinutes ?? 0,
                    index,
                    true,
                    null);
            })
            .ToArray();

        return new TrainingPathResponseModel(
            path.Id, path.Title, path.Slug, path.Description, path.Icon,
            path.IsAutoAssigned, path.IsActive, pathModules);
    }
}

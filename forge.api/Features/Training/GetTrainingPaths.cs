using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetTrainingPathsQuery(int UserId, bool IsAdmin, string? Lang = null) : IRequest<List<TrainingPathResponseModel>>;

public class GetTrainingPathsHandler(AppDbContext db)
    : IRequestHandler<GetTrainingPathsQuery, List<TrainingPathResponseModel>>
{
    public async Task<List<TrainingPathResponseModel>> Handle(
        GetTrainingPathsQuery request, CancellationToken ct)
    {
        var query = db.TrainingPaths
            .AsNoTracking()
            .Include(p => p.PathModules)
                .ThenInclude(pm => pm.Module)
            .AsQueryable();

        if (!request.IsAdmin)
            query = query.Where(p => p.IsActive);

        var paths = await query.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(ct);

        var allModuleIds = paths.SelectMany(p => p.PathModules.Select(pm => pm.ModuleId)).Distinct().ToList();
        var progressMap = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId && allModuleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, ct);

        var pathTr = await TrainingLocalization.PathTranslationsAsync(db, paths.Select(p => p.Id).ToList(), request.Lang, ct);
        var moduleTr = await TrainingLocalization.ModuleTranslationsAsync(db, allModuleIds, request.Lang, ct);

        return paths.Select(p => MapPath(p, progressMap, pathTr, moduleTr)).ToList();
    }

    internal static TrainingPathResponseModel MapPath(
        Forge.Core.Entities.TrainingPath path,
        Dictionary<int, Forge.Core.Entities.TrainingProgress> progressMap,
        Dictionary<int, Forge.Core.Entities.TrainingPathTranslation> pathTr,
        Dictionary<int, Forge.Core.Entities.TrainingModuleTranslation> moduleTr)
    {
        var modules = path.PathModules
            .OrderBy(pm => pm.Position)
            .Select(pm =>
            {
                progressMap.TryGetValue(pm.ModuleId, out var prog);
                return new TrainingPathModuleResponseModel(
                    pm.ModuleId,
                    moduleTr.GetValueOrDefault(pm.ModuleId)?.Title ?? pm.Module.Title,
                    pm.Module.ContentType,
                    pm.Module.EstimatedMinutes,
                    pm.Position,
                    pm.IsRequired,
                    prog?.Status
                );
            }).ToArray();

        return new TrainingPathResponseModel(
            path.Id,
            pathTr.GetValueOrDefault(path.Id)?.Title ?? path.Title,
            path.Slug,
            pathTr.GetValueOrDefault(path.Id)?.Description ?? path.Description,
            path.Icon,
            path.IsAutoAssigned,
            path.IsActive,
            modules
        );
    }
}

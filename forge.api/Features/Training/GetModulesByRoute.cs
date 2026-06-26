using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetModulesByRouteQuery(string Route, int UserId, string? Lang = null) : IRequest<List<TrainingModuleListItemResponseModel>>;

public class GetModulesByRouteHandler(AppDbContext db)
    : IRequestHandler<GetModulesByRouteQuery, List<TrainingModuleListItemResponseModel>>
{
    public async Task<List<TrainingModuleListItemResponseModel>> Handle(
        GetModulesByRouteQuery request, CancellationToken ct)
    {
        var modules = await db.TrainingModules
            .AsNoTracking()
            .Where(m => m.IsPublished && m.AppRoutes != null)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        // Filter in-process: any route in AppRoutes starts with or equals the query route
        var route = request.Route.TrimEnd('/');
        var matched = modules
            .Where(m =>
            {
                var routes = ParseJsonStringArray(m.AppRoutes);
                return routes.Any(r => route.StartsWith(r.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                                    || r.TrimEnd('/').StartsWith(route, StringComparison.OrdinalIgnoreCase));
            })
            .Take(10)
            .ToList();

        var moduleIds = matched.Select(m => m.Id).ToList();
        var progressMap = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId && moduleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, ct);

        var tr = await TrainingLocalization.ModuleTranslationsAsync(db, moduleIds, request.Lang, ct);

        return matched.Select(m =>
        {
            progressMap.TryGetValue(m.Id, out var prog);
            var t = tr.GetValueOrDefault(m.Id);
            return new TrainingModuleListItemResponseModel(
                m.Id,
                t?.Title ?? m.Title,
                m.Slug,
                t?.Summary ?? m.Summary,
                m.ContentType,
                m.CoverImageUrl,
                m.EstimatedMinutes,
                ParseJsonStringArray(m.Tags),
                m.IsPublished,
                m.IsOnboardingRequired,
                m.SortOrder,
                prog?.Status,
                prog?.QuizScore,
                prog?.CompletedAt
            );
        }).ToList();
    }

    private static string[] ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}

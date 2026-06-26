using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record TrainingPaginatedResult<T>(List<T> Data, int Page, int PageSize, int TotalCount, int TotalPages);

public record GetTrainingModulesQuery(
    int UserId,
    bool IsAdmin,
    string? Search,
    string? ContentType,
    string? Tag,
    bool IncludeUnpublished,
    int Page,
    int PageSize,
    string? Lang = null) : IRequest<TrainingPaginatedResult<TrainingModuleListItemResponseModel>>;

public class GetTrainingModulesHandler(AppDbContext db)
    : IRequestHandler<GetTrainingModulesQuery, TrainingPaginatedResult<TrainingModuleListItemResponseModel>>
{
    public async Task<TrainingPaginatedResult<TrainingModuleListItemResponseModel>> Handle(
        GetTrainingModulesQuery request, CancellationToken ct)
    {
        var query = db.TrainingModules.AsNoTracking().AsQueryable();

        if (!request.IsAdmin || !request.IncludeUnpublished)
            query = query.Where(m => m.IsPublished);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(m => EF.Functions.ILike(m.Title, $"%{request.Search}%"));

        if (!string.IsNullOrWhiteSpace(request.ContentType) &&
            Enum.TryParse<TrainingContentType>(request.ContentType, ignoreCase: true, out var contentTypeEnum))
            query = query.Where(m => m.ContentType == contentTypeEnum);

        var modules = await query.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(ct);

        // Filter by tag in-process (JSON array stored as string)
        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            modules = modules.Where(m =>
            {
                var tags = ParseJsonStringArray(m.Tags);
                return tags.Any(t => string.Equals(t, request.Tag, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        // Load current user progress
        var moduleIds = modules.Select(m => m.Id).ToList();
        var progressMap = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId && moduleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, ct);

        var totalCount = modules.Count;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 25 : Math.Min(request.PageSize, 100);
        var paged = modules.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var tr = await TrainingLocalization.ModuleTranslationsAsync(db, paged.Select(m => m.Id).ToList(), request.Lang, ct);

        var items = paged.Select(m =>
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

        return new TrainingPaginatedResult<TrainingModuleListItemResponseModel>(
            items, page, pageSize, totalCount, (int)Math.Ceiling((double)totalCount / pageSize));
    }

    private static string[] ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}

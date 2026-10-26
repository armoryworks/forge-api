using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Indented, multi-level where-used: every assembly whose current BOM uses this part directly or
/// through sub-assemblies, in depth-first order with direct parents at level 1, each with the
/// number of open work orders building that parent. Parents at each level are ordered by part
/// number. A parent already on the path is skipped so a BOM cycle cannot loop, the walk stops
/// at <see cref="GetPartWhereUsedHandler.MaxDepth"/> levels, and at most
/// <see cref="GetPartWhereUsedHandler.MaxRows"/> rows are returned.
/// </summary>
public record GetPartWhereUsedQuery(int PartId) : IRequest<List<PartWhereUsedResponseModel>>;

public class GetPartWhereUsedHandler(AppDbContext db)
    : IRequestHandler<GetPartWhereUsedQuery, List<PartWhereUsedResponseModel>>
{
    public const int MaxDepth = 25;
    public const int MaxRows = 5000;

    private sealed record UsageLine(
        int Id,
        int ChildPartId,
        int ParentPartId,
        string PartNumber,
        string Name,
        string Revision,
        decimal Quantity,
        BOMSourceType SourceType);

    public async Task<List<PartWhereUsedResponseModel>> Handle(GetPartWhereUsedQuery request, CancellationToken ct)
    {
        var root = await db.Parts.AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var usages = await LoadUsagesAsync(root.Id, ct);
        if (usages.Count == 0)
            return [];

        var parentIds = usages.Values.SelectMany(l => l).Select(l => l.ParentPartId).Distinct().ToList();
        var openCounts = await db.Jobs
            .AsNoTracking()
            .Where(j => j.PartId != null && parentIds.Contains(j.PartId.Value)
                && !j.IsArchived && j.CompletedDate == null && j.Disposition == null)
            .GroupBy(j => j.PartId!.Value)
            .Select(g => new { PartId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PartId, x => x.Count, ct);

        var result = new List<PartWhereUsedResponseModel>();
        var pathIds = new HashSet<int> { root.Id };
        var pathNumbers = new List<string> { root.PartNumber };
        Walk(root.Id, 1, usages, openCounts, pathIds, pathNumbers, result);
        return result;
    }

    private async Task<Dictionary<int, List<UsageLine>>> LoadUsagesAsync(int rootId, CancellationToken ct)
    {
        var usages = new Dictionary<int, List<UsageLine>>();
        var frontier = new List<int> { rootId };
        for (var depth = 0; depth < MaxDepth && frontier.Count > 0; depth++)
        {
            var lines = await db.BOMLines
                .AsNoTracking()
                .Where(b => frontier.Contains(b.ChildPartId) && b.ParentPart.DeletedAt == null)
                .Select(b => new UsageLine(
                    b.Id,
                    b.ChildPartId,
                    b.ParentPartId,
                    b.ParentPart.PartNumber,
                    b.ParentPart.Name,
                    b.ParentPart.Revision,
                    b.Quantity,
                    b.SourceType))
                .ToListAsync(ct);

            foreach (var childId in frontier)
                usages[childId] = [];
            foreach (var line in lines)
                usages[line.ChildPartId].Add(line);

            frontier = lines
                .Select(l => l.ParentPartId)
                .Distinct()
                .Where(id => !usages.ContainsKey(id))
                .ToList();
        }

        foreach (var key in usages.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            usages.Remove(key);
        foreach (var list in usages.Values)
            list.Sort((a, b) =>
            {
                var byNumber = string.CompareOrdinal(a.PartNumber, b.PartNumber);
                return byNumber != 0 ? byNumber : a.Id.CompareTo(b.Id);
            });
        return usages;
    }

    private static void Walk(
        int childId,
        int level,
        Dictionary<int, List<UsageLine>> usages,
        Dictionary<int, int> openCounts,
        HashSet<int> pathIds,
        List<string> pathNumbers,
        List<PartWhereUsedResponseModel> result)
    {
        if (level > MaxDepth || !usages.TryGetValue(childId, out var lines))
            return;

        foreach (var line in lines)
        {
            if (result.Count >= MaxRows)
                return;
            if (!pathIds.Add(line.ParentPartId))
                continue;

            pathNumbers.Add(line.PartNumber);
            result.Add(new PartWhereUsedResponseModel(
                line.Id,
                line.ParentPartId,
                line.PartNumber,
                line.Name,
                line.Revision,
                line.Quantity,
                line.SourceType,
                openCounts.GetValueOrDefault(line.ParentPartId),
                level,
                pathNumbers.ToList()));

            Walk(line.ParentPartId, level + 1, usages, openCounts, pathIds, pathNumbers, result);

            pathNumbers.RemoveAt(pathNumbers.Count - 1);
            pathIds.Remove(line.ParentPartId);
        }
    }
}

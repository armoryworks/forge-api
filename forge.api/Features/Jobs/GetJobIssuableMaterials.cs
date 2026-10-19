using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobIssuableMaterialsQuery(int JobId) : IRequest<List<JobIssuableMaterialResponseModel>>;

public class GetJobIssuableMaterialsHandler(AppDbContext db)
    : IRequestHandler<GetJobIssuableMaterialsQuery, List<JobIssuableMaterialResponseModel>>
{
    public async Task<List<JobIssuableMaterialResponseModel>> Handle(
        GetJobIssuableMaterialsQuery request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.Id, j.PartId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");
        if (job.PartId is not int jobPartId)
            return [];

        var components = await JobMaterialRules.ForParentAsync(db, jobPartId, cancellationToken);
        if (components.Count == 0)
            return [];

        var built = await db.JobParts.AsNoTracking()
            .Where(jp => jp.JobId == job.Id && jp.PartId == jobPartId)
            .SumAsync(jp => jp.Quantity, cancellationToken);
        var buildQuantity = built > 0m ? built : 1m;

        var partIds = components.Select(c => c.IssuedPartId)
            .Concat(components.Where(c => c.AlternateForPartId.HasValue).Select(c => c.AlternateForPartId!.Value))
            .Distinct()
            .ToList();
        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PartNumber, p.Description })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var issued = await db.MaterialIssues.AsNoTracking()
            .Where(mi => mi.JobId == job.Id && partIds.Contains(mi.PartId) && mi.IssueType != MaterialIssueType.Scrap)
            .GroupBy(mi => mi.PartId)
            .Select(g => new
            {
                PartId = g.Key,
                Net = g.Sum(mi => mi.IssueType == MaterialIssueType.Return ? -mi.Quantity : mi.Quantity),
            })
            .ToDictionaryAsync(x => x.PartId, x => x.Net, cancellationToken);

        return components
            .GroupBy(c => (c.IssuedPartId, c.AlternateForPartId))
            .Where(g => parts.ContainsKey(g.Key.IssuedPartId))
            .Select(g =>
            {
                var part = parts[g.Key.IssuedPartId];
                var perUnit = g.Sum(c => c.PerUnit);
                return new JobIssuableMaterialResponseModel(
                    part.Id, part.PartNumber, part.Description, perUnit, perUnit * buildQuantity,
                    issued.GetValueOrDefault(part.Id), g.Key.AlternateForPartId,
                    g.Key.AlternateForPartId is int primary && parts.TryGetValue(primary, out var p) ? p.PartNumber : null);
            })
            .OrderBy(m => m.AlternateForPartId.HasValue)
            .ThenBy(m => m.PartNumber)
            .ToList();
    }
}

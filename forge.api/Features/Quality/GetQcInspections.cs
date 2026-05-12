using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetQcInspectionsQuery(int? JobId, string? Status, string? LotNumber) : IRequest<List<QcInspectionResponseModel>>;

public class GetQcInspectionsHandler(AppDbContext db)
    : IRequestHandler<GetQcInspectionsQuery, List<QcInspectionResponseModel>>
{
    public async Task<List<QcInspectionResponseModel>> Handle(
        GetQcInspectionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.QcInspections
            .AsNoTracking()
            .Include(i => i.Results)
            .Include(i => i.Job)
            .Include(i => i.Template)
            .AsQueryable();

        if (request.JobId.HasValue)
            query = query.Where(i => i.JobId == request.JobId.Value);

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(i => i.Status == request.Status);

        if (!string.IsNullOrWhiteSpace(request.LotNumber))
            query = query.Where(i => i.LotNumber != null && i.LotNumber.Contains(request.LotNumber));

        // Pre-load inspector names to avoid N+1 subquery inside Select projection
        var inspections = await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var inspectorIds = inspections
            .Select(i => i.InspectorId)
            .Distinct()
            .ToList();

        var inspectorNames = inspectorIds.Count > 0
            ? await db.Users
                .Where(u => inspectorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken)
            : new Dictionary<int, string>();

        return inspections.Select(i => new QcInspectionResponseModel(
            i.Id,
            i.JobId,
            i.Job != null ? i.Job.JobNumber : null,
            i.ProductionRunId,
            i.TemplateId,
            i.Template != null ? i.Template.Name : null,
            i.InspectorId,
            inspectorNames.TryGetValue(i.InspectorId, out var name) ? name : "",
            i.LotNumber,
            i.Status,
            i.Notes,
            i.CompletedAt,
            i.Results.Select(r => new QcInspectionResultModel(
                r.Id,
                r.ChecklistItemId,
                r.Description,
                r.Passed,
                r.MeasuredValue,
                r.Notes
            )).ToList(),
            i.CreatedAt))
        .ToList();
    }
}

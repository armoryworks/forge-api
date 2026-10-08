using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetQcInspectionsQuery(int? JobId, string? Status, string? LotNumber, string? Search)
    : IRequest<List<QcInspectionResponseModel>>;

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
            .Include(i => i.Part)
            .AsQueryable();

        if (request.JobId.HasValue)
            query = query.Where(i => i.JobId == request.JobId.Value);

        var statuses = (request.Status ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (statuses.Count > 0)
            query = query.Where(i => statuses.Contains(i.Status));

        if (!string.IsNullOrWhiteSpace(request.LotNumber))
            query = query.Where(i => i.LotNumber != null && i.LotNumber.Contains(request.LotNumber));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(i =>
                (i.LotNumber != null && i.LotNumber.ToLower().Contains(term))
                || (i.Job != null && i.Job.JobNumber.ToLower().Contains(term))
                || (i.Part != null && i.Part.PartNumber.ToLower().Contains(term))
                || (i.PartId == null && i.Job != null && i.Job.Part != null && i.Job.Part.PartNumber.ToLower().Contains(term)));
        }

        var inspections = await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return await QcInspectionMapping.ToResponseModelsAsync(db, inspections, cancellationToken);
    }
}

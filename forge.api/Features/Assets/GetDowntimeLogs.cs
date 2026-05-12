using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record GetDowntimeLogsQuery(int? AssetId) : IRequest<List<DowntimeLogResponseModel>>;

public class GetDowntimeLogsHandler(AppDbContext db) : IRequestHandler<GetDowntimeLogsQuery, List<DowntimeLogResponseModel>>
{
    public async Task<List<DowntimeLogResponseModel>> Handle(GetDowntimeLogsQuery request, CancellationToken cancellationToken)
    {
        var query = db.DowntimeLogs
            .Include(d => d.Asset)
            .AsNoTracking();

        if (request.AssetId.HasValue)
            query = query.Where(d => d.AssetId == request.AssetId.Value);

        return await query
            .OrderByDescending(d => d.StartedAt)
            .Select(d => new DowntimeLogResponseModel(
                d.Id,
                d.AssetId,
                d.Asset.Name,
                d.ReportedById,
                d.StartedAt,
                d.EndedAt,
                d.Reason,
                d.Resolution,
                d.IsPlanned,
                d.Notes,
                d.EndedAt.HasValue
                    ? (decimal)(d.EndedAt.Value - d.StartedAt).TotalHours
                    : (decimal)(DateTimeOffset.UtcNow - d.StartedAt).TotalHours,
                d.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

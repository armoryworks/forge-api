using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record GetTimeCorrectionsQuery(int? UserId, DateOnly? From, DateOnly? To)
    : IRequest<List<TimeCorrectionLogResponseModel>>;

public class GetTimeCorrectionsHandler(AppDbContext db)
    : IRequestHandler<GetTimeCorrectionsQuery, List<TimeCorrectionLogResponseModel>>
{
    public async Task<List<TimeCorrectionLogResponseModel>> Handle(
        GetTimeCorrectionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.TimeCorrectionLogs
            .Include(c => c.TimeEntry)
            .AsQueryable();

        if (request.UserId.HasValue)
            query = query.Where(c => c.TimeEntry.UserId == request.UserId.Value);

        if (request.From.HasValue)
            query = query.Where(c => c.OriginalDate >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(c => c.OriginalDate <= request.To.Value);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new TimeCorrectionLogResponseModel(
                c.Id,
                c.TimeEntryId,
                c.CorrectedByUserId,
                db.Users
                    .Where(u => u.Id == c.CorrectedByUserId)
                    .Select(u => u.LastName + ", " + u.FirstName)
                    .FirstOrDefault() ?? "",
                c.Reason,
                c.OriginalJobId,
                c.OriginalJobId != null
                    ? db.Jobs.Where(j => j.Id == c.OriginalJobId).Select(j => j.JobNumber).FirstOrDefault()
                    : null,
                c.OriginalDate,
                c.OriginalDurationMinutes,
                c.OriginalStartTime,
                c.OriginalEndTime,
                c.OriginalCategory,
                c.OriginalNotes,
                c.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

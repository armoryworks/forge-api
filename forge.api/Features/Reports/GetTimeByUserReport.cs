using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetTimeByUserReportQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<TimeByUserReportItem>>;

public class GetTimeByUserReportHandler(IReportRepository repo, AppDbContext db) : IRequestHandler<GetTimeByUserReportQuery, List<TimeByUserReportItem>>
{
    public async Task<List<TimeByUserReportItem>> Handle(GetTimeByUserReportQuery request, CancellationToken cancellationToken)
    {
        var items = await repo.GetTimeByUserAsync(request.Start, request.End, cancellationToken);

        var userIds = items.Select(i => i.UserId).ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        return items.Select(i => i with { UserName = users.GetValueOrDefault(i.UserId, "Unknown") }).ToList();
    }
}

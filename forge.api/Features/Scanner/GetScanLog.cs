using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanLogQuery(int? UserId, DateTimeOffset? Date, ScanActionType? ActionType) : IRequest<List<ScanLogEntryModel>>;

public class GetScanLogHandler(AppDbContext db)
    : IRequestHandler<GetScanLogQuery, List<ScanLogEntryModel>>
{
    public async Task<List<ScanLogEntryModel>> Handle(
        GetScanLogQuery request, CancellationToken cancellationToken)
    {
        var targetDate = request.Date ?? DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(targetDate.Date, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1);

        var query = db.ScanActionLogs
            .AsNoTracking()
            .Where(sal => sal.CreatedAt >= startOfDay && sal.CreatedAt < endOfDay);

        if (request.UserId.HasValue)
            query = query.Where(sal => sal.UserId == request.UserId.Value);

        if (request.ActionType.HasValue)
            query = query.Where(sal => sal.ActionType == request.ActionType.Value);

        var userLookup = await db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, Name = u.LastName + ", " + u.FirstName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var locationLookup = await db.StorageLocations
            .AsNoTracking()
            .Select(sl => new { sl.Id, sl.Name })
            .ToDictionaryAsync(sl => sl.Id, sl => sl.Name, cancellationToken);

        var logs = await query
            .OrderByDescending(sal => sal.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        return logs.Select(sal => new ScanLogEntryModel(
            sal.Id,
            sal.ActionType.ToString(),
            userLookup.TryGetValue(sal.UserId, out var userName) ? userName : "Unknown",
            sal.PartNumber,
            sal.Quantity,
            sal.FromLocationId.HasValue && locationLookup.TryGetValue(sal.FromLocationId.Value, out var fromName) ? fromName : null,
            sal.ToLocationId.HasValue && locationLookup.TryGetValue(sal.ToLocationId.Value, out var toName) ? toName : null,
            sal.RelatedEntityType != null ? $"{sal.RelatedEntityType} #{sal.RelatedEntityId}" : null,
            sal.IsReversed,
            sal.CreatedAt
        )).ToList();
    }
}

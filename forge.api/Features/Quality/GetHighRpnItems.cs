using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetHighRpnItemsQuery(int Threshold = 200) : IRequest<List<FmeaItemResponseModel>>;

public class GetHighRpnItemsHandler(AppDbContext db)
    : IRequestHandler<GetHighRpnItemsQuery, List<FmeaItemResponseModel>>
{
    public async Task<List<FmeaItemResponseModel>> Handle(
        GetHighRpnItemsQuery request, CancellationToken cancellationToken)
    {
        var items = await db.Set<FmeaItem>()
            .AsNoTracking()
            .Include(i => i.Fmea)
            .ToListAsync(cancellationToken);

        var highRpn = items
            .Where(i => i.Severity * i.Occurrence * i.Detection > request.Threshold)
            .OrderByDescending(i => i.Severity * i.Occurrence * i.Detection)
            .ToList();

        var userIds = highRpn
            .Where(i => i.ResponsibleUserId.HasValue)
            .Select(i => i.ResponsibleUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken)
            : new Dictionary<int, string>();

        return highRpn.Select(i => new FmeaItemResponseModel
        {
            Id = i.Id,
            ItemNumber = i.ItemNumber,
            ProcessStep = i.ProcessStep,
            Function = i.Function,
            FailureMode = i.FailureMode,
            PotentialEffect = i.PotentialEffect,
            Severity = i.Severity,
            Occurrence = i.Occurrence,
            Detection = i.Detection,
            Rpn = i.Severity * i.Occurrence * i.Detection,
            RecommendedAction = i.RecommendedAction,
            ResponsibleUserName = i.ResponsibleUserId.HasValue && userNames.TryGetValue(i.ResponsibleUserId.Value, out var n) ? n : null,
            TargetCompletionDate = i.TargetCompletionDate,
            ActionTaken = i.ActionTaken,
            ActionCompletedAt = i.ActionCompletedAt,
            RevisedSeverity = i.RevisedSeverity,
            RevisedOccurrence = i.RevisedOccurrence,
            RevisedDetection = i.RevisedDetection,
            RevisedRpn = i.RevisedSeverity.HasValue && i.RevisedOccurrence.HasValue && i.RevisedDetection.HasValue
                ? i.RevisedSeverity.Value * i.RevisedOccurrence.Value * i.RevisedDetection.Value
                : null,
            CapaId = i.CapaId,
        }).ToList();
    }
}

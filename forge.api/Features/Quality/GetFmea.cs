using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetFmeaQuery(int Id) : IRequest<FmeaResponseModel>;

public class GetFmeaHandler(AppDbContext db)
    : IRequestHandler<GetFmeaQuery, FmeaResponseModel>
{
    private const int HighRpnThreshold = 200;

    public async Task<FmeaResponseModel> Handle(
        GetFmeaQuery request, CancellationToken cancellationToken)
    {
        var f = await db.FmeaAnalyses
            .AsNoTracking()
            .Include(x => x.Part)
            .Include(x => x.Operation)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA {request.Id} not found");

        var userIds = f.Items
            .Where(i => i.ResponsibleUserId.HasValue)
            .Select(i => i.ResponsibleUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken)
            : new Dictionary<int, string>();

        var capaIds = f.Items
            .Where(i => i.CapaId.HasValue)
            .Select(i => i.CapaId!.Value)
            .Distinct()
            .ToList();

        var capaNumbers = capaIds.Count > 0
            ? await db.CorrectiveActions
                .Where(c => capaIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.CapaNumber, cancellationToken)
            : new Dictionary<int, string>();

        var rpns = f.Items.Select(i => i.Severity * i.Occurrence * i.Detection).ToList();

        return new FmeaResponseModel
        {
            Id = f.Id,
            FmeaNumber = f.FmeaNumber,
            Name = f.Name,
            Type = f.Type,
            PartId = f.PartId,
            PartNumber = f.Part?.PartNumber,
            OperationId = f.OperationId,
            OperationName = f.Operation?.Title,
            Status = f.Status,
            PreparedBy = f.PreparedBy,
            Responsibility = f.Responsibility,
            OriginalDate = f.OriginalDate,
            RevisionDate = f.RevisionDate,
            RevisionNumber = f.RevisionNumber,
            PpapSubmissionId = f.PpapSubmissionId,
            HighRpnCount = rpns.Count(r => r > HighRpnThreshold),
            MaxRpn = rpns.Count > 0 ? rpns.Max() : 0,
            Items = f.Items.OrderBy(i => i.ItemNumber).Select(i => new FmeaItemResponseModel
            {
                Id = i.Id,
                ItemNumber = i.ItemNumber,
                ProcessStep = i.ProcessStep,
                Function = i.Function,
                FailureMode = i.FailureMode,
                PotentialEffect = i.PotentialEffect,
                Severity = i.Severity,
                Classification = i.Classification,
                PotentialCause = i.PotentialCause,
                Occurrence = i.Occurrence,
                CurrentPreventionControls = i.CurrentPreventionControls,
                CurrentDetectionControls = i.CurrentDetectionControls,
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
                CapaCorrNum = i.CapaId.HasValue && capaNumbers.TryGetValue(i.CapaId.Value, out var cn) ? cn : null,
            }).ToList(),
            CreatedAt = f.CreatedAt,
        };
    }
}
